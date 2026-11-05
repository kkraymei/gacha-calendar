using System.Text.Json.Serialization;

namespace GachaCalendar.Core;

/// <summary>What an entry schedules. Collaborations are a flag, not a kind: a collab can be a banner or an event.</summary>
public enum EntryKind { Version, Banner, Event, Offline, Livestream, Maintenance }

/// <summary>One request that yields announcements. Placeholders like {id} are filled from list rows.</summary>
public sealed record SourceSpec
{
    public string Url { get; init; } = "";
    public string? Name { get; init; }
    public string Method { get; init; } = "GET";
    public Dictionary<string, string>? Headers { get; init; }
    public string? Body { get; init; }
    /// <summary>A second response whose rows are merged into the list rows by a shared key (e.g. notice bodies).</summary>
    public RequestSpec? Join { get; init; }
    /// <summary>Per-row request for the full announcement when the list carries titles only.</summary>
    public RequestSpec? Detail { get; init; }
    /// <summary>Human-readable page for entries; a template with row placeholders or a fixed URL.</summary>
    public string? Page { get; init; }
    /// <summary>The list's start/end fields describe when a notice is shown, not when the event runs.</summary>
    public bool StartIsPublished { get; init; }
}

public sealed record RequestSpec
{
    public string Url { get; init; } = "";
    public string Method { get; init; } = "GET";
    public Dictionary<string, string>? Headers { get; init; }
    public string? Body { get; init; }
    /// <summary>Join only: the field that pairs rows of both responses.</summary>
    public string? Key { get; init; }
    /// <summary>Detail only: how many of the newest relevant rows are expanded.</summary>
    public int Limit { get; init; } = 40;
}

public sealed record RegionTemplate(string Id, string Name, List<SourceSpec> Sources);

public sealed record GameTemplate(string Id, string Name, string Publisher, string Color, List<RegionTemplate> Regions);

/// <summary>A game server the user follows.</summary>
public sealed record GameConfig
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Region { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string Color { get; init; } = "#6B7A90";
    public List<SourceSpec> Sources { get; init; } = [];
    public bool Visible { get; init; } = true;
    /// <summary>"game/region" of the built-in template this came from; null for custom games.</summary>
    public string? Template { get; init; }
    /// <summary>The user edited a template game's sources; catalog updates no longer replace them.</summary>
    public bool CustomSources { get; init; }

    [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(Region) ? Name : $"{Name}（{Region}）";
}

public sealed record Origin(string Source, string Url, DateTime Start, DateTime End, bool Uncertain);

public sealed record Entry
{
    public string GameId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Summary { get; init; } = "";
    public EntryKind Kind { get; init; }
    public bool IsCollab { get; init; }
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
    /// <summary>Only a publication date was found; the schedule still needs confirming.</summary>
    public bool Uncertain { get; init; }
    public string Url { get; init; } = "";
    public string Source { get; init; } = "";
    /// <summary>Title of the digest notice (e.g. version notes) this entry was split from.</summary>
    public string? Parent { get; init; }
    public List<Origin> Origins { get; init; } = [];
    public bool DateConflict { get; init; }

    /// <summary>Stable identity for user marks: game, normalized title, start day.</summary>
    [JsonIgnore] public string Key => $"{GameId}|{Merger.TitleKey(Title)}|{Start:yyyyMMdd}";
    [JsonIgnore] public int Days => (End - Start).Days + 1;
}

public enum SourceState { Ok, Partial, Empty, Failed, Cached }

public sealed record SourceReport(string GameId, string Source, string Url, SourceState State, int Count, string Message, DateTime At);
