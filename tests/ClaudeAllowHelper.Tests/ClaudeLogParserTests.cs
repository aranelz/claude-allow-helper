using ClaudeAllowHelper.Core;

namespace ClaudeAllowHelper.Tests;

public class ClaudeLogParserTests
{
    [Fact]
    public void ParsesMultilineJavaScriptApprovalBlock()
    {
        var parser = new ClaudeLogParser();
        var batch = parser.Push("""
            2026-09-04 16:04:05 [info] [Preview] cat3 per-action approval requested {
              origin: 'http://analitikcms.test',
              toolName: 'javascript_tool',
              action: undefined
            }
            2026-09-04 16:04:05 [info] Emitted tool permission request 3a7c3c58-08b7-44ee-b589-c9c29b943b8a for browser:javascript_tool in session abc

            """);

        var evt = Assert.Single(batch.Permissions);
        Assert.Equal("http://analitikcms.test", evt.Origin);
        Assert.Equal("javascript_tool", evt.ToolName);
        Assert.Equal("3a7c3c58-08b7-44ee-b589-c9c29b943b8a", evt.RequestId);
        Assert.True(evt.IsJavaScriptTool);
    }

    [Fact]
    public void ParsesReadApprovalAsUnrelatedPermission()
    {
        var parser = new ClaudeLogParser();
        var batch = parser.Push("""
            2026-09-04 16:06:40 [info] [Preview] cat3 per-action read approval requested {
              origin: 'http://analitikcms.test',
              toolName: 'read_console_messages',
              offerAlwaysGrant: false
            }

            """);

        var evt = Assert.Single(batch.Permissions);
        Assert.Equal("read_console_messages", evt.ToolName);
        Assert.False(evt.IsJavaScriptTool);
    }

    [Fact]
    public void ParsesChunkedLines()
    {
        var parser = new ClaudeLogParser();
        Assert.Empty(parser.Push("2026-09-04 16:04:05 [info] [Preview] cat3 per-action approval requested {\n").Permissions);
        Assert.Empty(parser.Push("  origin: 'http://localhost',\n").Permissions);
        var batch = parser.Push("  toolName: 'javascript_tool',\n}\n");

        var evt = Assert.Single(batch.Permissions);
        Assert.Equal("http://localhost", evt.Origin);
        Assert.Equal("javascript_tool", evt.ToolName);
    }

    [Fact]
    public void RecordsHelperIngestTimeSeparatelyFromWholeSecondClaudeTimestamp()
    {
        var ingest = new DateTimeOffset(2026, 9, 4, 17, 8, 0, 173, TimeSpan.FromHours(3));
        var parser = new ClaudeLogParser();
        var batch = parser.Push("""
            2026-09-04 17:08:00 [info] [Preview] cat3 per-action approval requested {
              origin: 'http://analitikcms.test',
              toolName: 'javascript_tool',
              action: undefined
            }
            2026-09-04 17:08:00 [info] Emitted tool permission request e108a40e-7127-4ff5-b523-83d9d183e2ac for browser:javascript_tool in session abc

            """, ingest);

        var evt = Assert.Single(batch.Permissions);
        Assert.Equal("javascript_tool", evt.ToolName);
        Assert.Equal("e108a40e-7127-4ff5-b523-83d9d183e2ac", evt.RequestId);
        Assert.Equal(new DateTime(2026, 9, 4, 17, 8, 0), evt.ObservedAt.DateTime);
        Assert.Equal(0, evt.ObservedAt.Millisecond);
        Assert.Equal(ingest, evt.IngestedAt);
        Assert.Equal(ingest, evt.FreshnessTimestamp);
        Assert.NotEqual(evt.ObservedAt, evt.IngestedAt);
    }

    [Fact]
    public void ParsesPermissionResponse()
    {
        var parser = new ClaudeLogParser();
        var batch = parser.Push(
            "2026-09-04 16:04:06 [info] Received permission response for 3a7c3c58-08b7-44ee-b589-c9c29b943b8a: once (tool: browser:javascript_tool)\n");

        var response = Assert.Single(batch.Responses);
        Assert.Equal("3a7c3c58-08b7-44ee-b589-c9c29b943b8a", response.RequestId);
        Assert.Equal("once", response.Decision);
        Assert.Equal("javascript_tool", response.ToolName);
    }
}
