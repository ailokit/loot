using System.Text;

namespace Loot.AI;

/// <summary>Accumulates the response for persistence while returning every display update immediately.</summary>
internal sealed class StreamingResponseContent
{
    private readonly List<Segment> _segments = [];

    public ChatStreamUpdate AppendReasoning(string text)
    {
        var segment = GetOrCreateThinkingSegment();
        if (segment.EndsWithTool)
        {
            segment.Content.Append('\n');
        }

        segment.Content.Append(text);
        segment.EndsWithTool = false;
        return ChatStreamUpdate.Reasoning(text);
    }

    public ChatStreamUpdate AppendText(string text)
    {
        var segment = _segments.LastOrDefault();
        if (segment is null || segment.Kind != SegmentKind.Text)
        {
            segment = new Segment(SegmentKind.Text);
            _segments.Add(segment);
        }

        segment.Content.Append(text);
        return ChatStreamUpdate.Text(text);
    }

    public ChatStreamUpdate AppendToolStarted(string callId, string notice)
    {
        var segment = GetOrCreateThinkingSegment();
        if (segment.Content.Length > 0 && segment.Content[^1] != '\n')
        {
            segment.Content.Append('\n');
        }

        segment.Content.Append("<!-- loot-tool -->\n");
        segment.Content.Append(notice);
        segment.Content.Append("\n<!-- /loot-tool -->\n");
        segment.EndsWithTool = true;
        return ChatStreamUpdate.ToolStarted(callId, notice);
    }

    public ChatStreamUpdate AppendToolCompleted(string callId) => ChatStreamUpdate.ToolCompleted(callId);

    public string GetContent()
    {
        var content = new StringBuilder();
        foreach (var segment in _segments)
        {
            if (segment.Kind == SegmentKind.Text)
            {
                content.Append(segment.Content);
                continue;
            }

            if (content.Length > 0 && content[^1] != '\n')
            {
                content.Append('\n');
            }

            content.Append("````thinking\n");
            content.Append(segment.Content);
            if (segment.Content.Length == 0 || segment.Content[^1] != '\n')
            {
                content.Append('\n');
            }

            content.Append("````\n");
        }

        return content.ToString();
    }

    private Segment GetOrCreateThinkingSegment()
    {
        var segment = _segments.LastOrDefault();
        if (segment is not null && segment.Kind == SegmentKind.Thinking)
        {
            return segment;
        }

        segment = new Segment(SegmentKind.Thinking);
        _segments.Add(segment);
        return segment;
    }

    private enum SegmentKind
    {
        Text,
        Thinking
    }

    private sealed class Segment(SegmentKind kind)
    {
        public SegmentKind Kind { get; } = kind;
        public StringBuilder Content { get; } = new();
        public bool EndsWithTool { get; set; }
    }
}
