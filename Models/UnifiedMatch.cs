using System.Collections.Concurrent;

public class UnifiedMatch
{
    public string HomeOrig { get; set; }
    public string AwayOrig { get; set; }

    public ConcurrentDictionary<string, MatchSourceData> Sources { get; set; }
        = new ConcurrentDictionary<string, MatchSourceData>();

    public UnifiedMatch()
    {
        Sources.TryAdd("Forebet", null);
        Sources.TryAdd("Statarea", null);
        Sources.TryAdd("PredictZ", null);
        Sources.TryAdd("WinDrawWin", null);
        Sources.TryAdd("ZuluBet", null);
    }
}