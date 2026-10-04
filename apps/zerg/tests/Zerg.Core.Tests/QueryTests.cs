namespace Zerg.Core.Tests;

public class QueryTests
{
    [Theory]
    [InlineData("file=A.jsonl&offset=12", "A.jsonl", 12)]
    [InlineData("?file=A.jsonl&offset=12", "A.jsonl", 12)]
    [InlineData("file=&offset=12", null, 12)]             // blank value dropped
    [InlineData("file&offset=12", null, 12)]              // bare name dropped
    [InlineData("offset=12&offset=40", null, 12)]         // first wins
    [InlineData("offset=abc", null, 0)]
    [InlineData("offset=12.0", null, 0)]
    [InlineData("offset=+12", null, 12)]                  // '+' is a space, and int() trims
    [InlineData("offset=%2B12", null, 12)]
    [InlineData("offset=-3", null, -3)]
    [InlineData("&&file=A%20B.jsonl&&", "A B.jsonl", 0)]
    [InlineData("", null, 0)]
    public void Parses_like_parse_qs(string query, string? file, long offset)
    {
        var q = Query.Parse(query);
        Assert.Equal(file, Query.First(q, "file"));
        Assert.Equal(offset, Query.Int(Query.First(q, "offset"), 0));
    }
}
