namespace BroadcastSystem.Models;

/// <summary>
/// Admin tarafindan baslatilan canli bir oylama/anketi (!anket) temsil eder.
/// </summary>
public sealed class ActivePoll
{
    public string Question { get; init; } = string.Empty;
    public List<string> Options { get; init; } = new();
    public int[] Votes { get; init; } = Array.Empty<int>();
    public Dictionary<ulong, int> VoterChoicesBySid { get; } = new();
    public Dictionary<int, int> VoterChoicesBySlot { get; } = new();
    public DateTime EndsAtUtc { get; init; }
    public string StartedBy { get; init; } = "Admin";

    public int TotalVotes => Votes.Sum();

    public int RemainingSeconds => Math.Max(0, (int)Math.Ceiling((EndsAtUtc - DateTime.UtcNow).TotalSeconds));
}

