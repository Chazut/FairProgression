using System.Globalization;
using System.Text;
using FairProgression.Models;
using FairProgression.Services;

namespace FairProgression.Web;

public partial class Settings
{
    private sealed record Row(int Index, string Label, int Count, long Vanilla, long Before, long After, long Target, bool Floor);
    private static readonly string Styles = ReadAsset("Settings.css");
    private static readonly string Icon = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(ReadAsset("Icon.svg")));
    private ConfigDocument? document;
    private FairProgressionConfig draft = new();
    private RewardPlan? plan;
    private List<Row> rows = [];
    private List<QuestInfo> SelectedQuests = [];
    private IEnumerable<string> Traders => SelectedQuests.Select(q => q.TraderName).Distinct().Order();
    private long chartMax;
    private string trader = "", manual = "", status = "";
    private string? error;
    private bool automatic;
    private int? selection, gap;
    private double? floorPercent;
    private bool LegacyMode => draft.Mode == "scale_modded_only" && !draft.ExtraXpPercent.HasValue;

    private string Mode
    {
        get => draft.Mode;
        set
        {
            draft.Mode = value;
            if (value == "scale_modded_only" && document?.Settings.Mode != value)
                draft.ExtraXpPercent ??= 0;
            Refresh();
        }
    }

    private int Share
    {
        get => draft.ExtraXpPercent ?? 0;
        set { draft.ExtraXpPercent = value; Refresh(); }
    }

    protected override void OnInitialized() => Reload();

    private static string ReadAsset(string name)
    {
        using var stream = typeof(Settings).Assembly.GetManifestResourceStream("FairProgression." + name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
    private string Width(long xp) => FormattableString.Invariant($"width:{100d * xp / chartMax:0.###}%");
    private void UseBudget() => Share = 0;

    private void Reload()
    {
        try
        {
            document = ConfigDocument.Open(ConfigLoader.ConfigPath);
            draft = document.Settings with { };
            automatic = string.Equals(draft.LevelBrackets.Trim(), "trader", StringComparison.OrdinalIgnoreCase);
            manual = automatic ? string.Join(", ", BracketResolver.Resolve("trader", Path.GetFullPath(Path.Combine(ConfigLoader.GetConfigDir(), "../../../.."))))
                : draft.LevelBrackets.Trim().Trim('[', ']');
            floorPercent = draft.MinCoefficient * 100;
            gap = draft.BuffGapThreshold;
            selection = null;
            Refresh();
            status = "";
        }
        catch (Exception ex) { document = null; status = $"Could not open settings. {ex.Message}"; }
    }

    private void Refresh()
    {
        error = null;
        try
        {
            draft.LevelBrackets = automatic ? "trader" : "[" + manual.Trim() + "]";
            if (!floorPercent.HasValue || !gap.HasValue)
                throw new ArgumentException("Enter a reward minimum and a difficulty gap.");
            draft.MinCoefficient = floorPercent.Value / 100;
            draft.BuffGapThreshold = gap.Value;
            ConfigLoader.Validate(draft);
            plan = ProgressionSnapshot.Current?.Preview(draft);
            if (plan == null) return;

            rows = plan.Brackets.Select((b, i) => new Row(i, b.Label, b.VanillaQuestCount + b.ModdedQuestCount,
                b.VanillaXp, b.VanillaXp + b.RawModdedXp, b.TotalXpAfter, b.TargetXp, b.FloorApplied)).ToList();
            var endgame = plan.Quests.Where(q => q.BracketIndex < 0).ToList();
            if (endgame.Count > 0)
            {
                long total = endgame.Sum(q => (long)q.BaseXp);
                rows.Add(new(-1, $"{(long)plan.Brackets[^1].UpperBound + 1}+", endgame.Count,
                    endgame.Where(q => q.IsVanilla).Sum(q => (long)q.BaseXp), total, total, total, false));
            }
            chartMax = Math.Max(1, rows.Max(r => Math.Max(r.Before, r.After)));
            if (rows.All(r => r.Index != selection)) selection = null;
            UpdateDetails();
            status = "";
        }
        catch (ArgumentException ex) { error = ex.Message; plan = null; status = ""; }
    }

    private void Select(int index)
    {
        selection = selection == index ? null : index;
        trader = "";
        UpdateDetails();
    }

    private void UpdateDetails()
    {
        SelectedQuests = plan!.Quests.Where(q => q.BracketIndex == selection)
            .OrderBy(q => q.TraderName).ThenBy(q => q.Name).ToList();
        if (trader.Length > 0 && !Traders.Contains(trader)) trader = "";
    }

    private string Explain(QuestInfo q)
    {
        if (q.BracketIndex < 0) return "Outside the configured brackets";
        if (q.IsVanilla && draft.Mode == "scale_modded_only") return "Vanilla reward protected";
        if (q.PreBuffXp > 0 && (plan!.Brackets[q.BracketIndex].Coefficient < 1 || draft.ExtraXpPercent > 0))
            return $"{q.MainObjective ?? "Quest objectives"} (estimated level {q.LevelFromDifficulty}): boost to {Number(q.OriginalXp)} XP" + (q.ScaledXp < q.OriginalXp ? ", then bracket scaling" : "");
        return q.ScaledXp != q.BaseXp ? "Bracket scaling" : "Original reward";
    }

    private void Save()
    {
        if (document == null || error != null) return;
        try
        {
            document = ConfigLoader.Save(ConfigLoader.ConfigPath, document, draft);
            status = "Settings saved.";
        }
        catch (Exception ex) { status = ex.Message; }
    }
}
