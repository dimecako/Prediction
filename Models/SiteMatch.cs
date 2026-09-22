public class SiteMatch
{
    public string SiteName { get; set; }
    public string HomeTeam { get; set; }
    public string AwayTeam { get; set; }
    public string Tip { get; set; }
    public int? Prob { get; set; }

    public string Score { get; set; } = "-";
    public string Btts { get; set; } = "-";
    public string BttsMarket { get; set; } = "-";
    public string GoalsMarket { get; set; } = "-";
}