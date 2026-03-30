using System.Text.RegularExpressions;

namespace Kode.Agent.Sdk.Core.Context;

/// <summary>
/// A document scored by BM25 against a query.
/// </summary>
public record BM25Result(string DocumentId, double Score, string Snippet, long Timestamp);

/// <summary>
/// In-memory BM25 index for searching compressed history windows.
///
/// Tokenization strategy:
///   - ASCII words: split on whitespace and punctuation (case-insensitive)
///   - CJK text: sliding bigrams (每两个相邻汉字作为一个 token)
///     e.g. "数据库" → ["数据", "据库"]
///   This avoids a dependency on a full CJK word segmentation library while
///   still providing reasonable recall for Chinese/Japanese/Korean queries.
///
/// BM25 parameters (Okapi BM25 standard defaults):
///   k1 = 1.5  — term-frequency saturation (diminishing returns after repeated occurrences)
///   b  = 0.75 — document-length normalisation
///
/// References:
///   - Robertson et al. "The Probabilistic Relevance Framework: BM25 and Beyond"
///   - Wikipedia: Okapi BM25
/// </summary>
public sealed class BM25Index
{
    private const double K1 = 1.5;
    private const double B = 0.75;

    // Indexed document storage
    private readonly List<IndexedDocument> _docs = [];
    private readonly Dictionary<string, int> _docFrequency = new(StringComparer.OrdinalIgnoreCase);
    private double _avgDocLength;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a document to the index.
    /// </summary>
    public void Add(string id, string text, string snippet, long timestamp)
    {
        var tokens = Tokenize(text);
        var tf = BuildTermFrequency(tokens);

        foreach (var term in tf.Keys)
        {
            _docFrequency[term] = _docFrequency.GetValueOrDefault(term) + 1;
        }

        _docs.Add(new IndexedDocument(id, tokens.Count, tf, snippet, timestamp));
        _avgDocLength = _docs.Average(d => d.Length);
    }

    /// <summary>
    /// Searches the index and returns the top <paramref name="limit"/> results ordered by score descending.
    /// Returns an empty list when the index is empty or the query produces no tokens.
    /// </summary>
    public IReadOnlyList<BM25Result> Search(string query, int limit = 5)
    {
        if (_docs.Count == 0) return [];

        var queryTokens = Tokenize(query);
        if (queryTokens.Count == 0) return [];

        var n = _docs.Count;
        var results = new List<(IndexedDocument doc, double score)>(_docs.Count);

        foreach (var doc in _docs)
        {
            var score = 0.0;
            foreach (var term in queryTokens.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!doc.TermFrequency.TryGetValue(term, out var tf)) continue;

                var df = _docFrequency.GetValueOrDefault(term, 0);
                var idf = Math.Log((n - df + 0.5) / (df + 0.5) + 1.0);
                var normTf = (tf * (K1 + 1)) / (tf + K1 * (1 - B + B * doc.Length / _avgDocLength));
                score += idf * normTf;
            }

            if (score > 0)
                results.Add((doc, score));
        }

        return results
            .OrderByDescending(r => r.score)
            .Take(limit)
            .Select(r => new BM25Result(r.doc.Id, r.score, r.doc.Snippet, r.doc.Timestamp))
            .ToList();
    }

    /// <summary>Number of documents in the index.</summary>
    public int Count => _docs.Count;

    // ── Tokenization ──────────────────────────────────────────────────────────

    /// <summary>
    /// Splits text into tokens.
    /// ASCII words are lowercased and split on non-alphanumeric characters.
    /// CJK characters are emitted as bigrams (2-character sliding window).
    /// </summary>
    internal static List<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var tokens = new List<string>();
        var asciiBuffer = new System.Text.StringBuilder();

        void FlushAscii()
        {
            if (asciiBuffer.Length == 0) return;
            var word = asciiBuffer.ToString().Trim();
            if (word.Length > 0)
                tokens.Add(word.ToLowerInvariant());
            asciiBuffer.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (IsCjk(c))
            {
                FlushAscii();
                // Emit bigram: current + next CJK char
                if (i + 1 < text.Length && IsCjk(text[i + 1]))
                    tokens.Add(new string([c, text[i + 1]]));
                // Also emit single char so "数据" matches query "数"
                tokens.Add(c.ToString());
            }
            else if (char.IsLetterOrDigit(c))
            {
                asciiBuffer.Append(c);
            }
            else
            {
                FlushAscii();
            }
        }
        FlushAscii();

        return tokens;
    }

    private static bool IsCjk(char c) =>
        (c >= 0x4E00 && c <= 0x9FFF)  // CJK Unified Ideographs
     || (c >= 0x3040 && c <= 0x30FF)  // Hiragana + Katakana
     || (c >= 0xAC00 && c <= 0xD7AF); // Hangul Syllables

    private static Dictionary<string, int> BuildTermFrequency(List<string> tokens)
    {
        var tf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
            tf[token] = tf.GetValueOrDefault(token) + 1;
        return tf;
    }

    // ── Internal record ───────────────────────────────────────────────────────

    private sealed record IndexedDocument(
        string Id,
        int Length,
        Dictionary<string, int> TermFrequency,
        string Snippet,
        long Timestamp);
}
