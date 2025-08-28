namespace Summarizer.Model;

public sealed record PolicyPatch(string Type, string Path, string Rule, IDictionary<string,string> Params);

public sealed record SummaryResponse(
    string Summary,
    string ProbableCause,
    PolicyPatch? SuggestedPolicyPatch,
    double Confidence,
    string[] NextSteps);
