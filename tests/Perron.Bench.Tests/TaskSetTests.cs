using System.Text.Json;
using System.Text.RegularExpressions;

namespace Perron.Bench.Tests;

/// <summary>
/// Format and coverage rules for bench/tasks/dev.jsonl and test.jsonl (plan.md, section 6).
/// Failure messages name task IDs and rules only, never prompts, so test.jsonl is not revealed.
/// </summary>
public partial class TaskSetTests
{
    private static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "tasks");

    private static readonly string[] Categories =
        ["stops", "trip_simple", "trip_constraints", "departures", "multistep", "ambiguous", "bad_input", "fr", "extended"];

    private static readonly string[] Capabilities = ["stops", "trips", "departures", "occupancy", "disruptions"];

    private static readonly string[] JudgeCategories = ["ambiguous", "bad_input"];

    private static readonly Dictionary<string, string[]> OracleRequiredParams = new()
    {
        ["next_departure"] = ["stop", "after"],
        ["earliest_arrival"] = ["from", "to", "depart_after"],
        ["latest_departure"] = ["from", "to", "arrive_before"],
        ["stop_candidates"] = ["name"],
        ["occupancy"] = ["from", "to", "depart_after", "class"],
    };

    private static readonly Dictionary<string, string[]> OracleOptionalParams = new()
    {
        ["next_departure"] = ["line", "towards", "transport_type", "type"],
        ["earliest_arrival"] = ["via", "min_transfer_minutes", "transport_types"],
        ["latest_departure"] = ["via", "min_transfer_minutes", "transport_types"],
        ["stop_candidates"] = [],
        ["occupancy"] = [],
    };

    private static readonly string[] TimeParams = ["after", "depart_after", "arrive_before"];

    public static TheoryData<string> Sets() => new("dev", "test");

    [Theory]
    [MemberData(nameof(Sets))]
    public void EveryTask_FollowsTheFormat(string set)
    {
        var violations = Load(set).SelectMany(task => Validate(set, task)).ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("dev", 20, 30)]
    [InlineData("test", 15, 25)]
    public void Set_HasTheIntendedSize_AndCoversEveryCategory(string set, int min, int max)
    {
        var tasks = Load(set);

        Assert.InRange(tasks.Count, min, max);
        var missing = Categories.Except(tasks.Select(task => Text(task, "category"))).ToList();
        Assert.True(missing.Count == 0, $"{set}: categories without tasks: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Smoke_IsAboutSixDevTasks_AndNeverInTest()
    {
        var devSmoke = Load("dev").Count(task => task.GetProperty("smoke").GetBoolean());
        var testSmoke = Load("test").Where(task => task.GetProperty("smoke").GetBoolean()).Select(task => Text(task, "id")).ToList();

        Assert.InRange(devSmoke, 4, 8);
        Assert.True(testSmoke.Count == 0, $"smoke tasks in test: {string.Join(", ", testSmoke)}");
    }

    [Fact]
    public void Ids_AndPrompts_AreUniqueAcrossBothSets()
    {
        var all = Load("dev").Concat(Load("test")).ToList();

        var duplicateIds = all.GroupBy(task => Text(task, "id")).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
        Assert.True(duplicateIds.Count == 0, $"duplicate ids: {string.Join(", ", duplicateIds)}");

        var duplicatePrompts = all
            .GroupBy(task => Whitespace().Replace(Text(task, "prompt").ToLowerInvariant(), " ").Trim())
            .Where(group => group.Count() > 1)
            .Select(group => string.Join("/", group.Select(task => Text(task, "id"))))
            .ToList();
        Assert.True(duplicatePrompts.Count == 0, $"tasks with identical prompts: {string.Join(", ", duplicatePrompts)}");
    }

    private static IEnumerable<string> Validate(string set, JsonElement task)
    {
        var id = task.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
            ? idElement.GetString()!
            : "<no id>";

        if (!Regex.IsMatch(id, $"^{set}-\\d{{3}}$"))
        {
            yield return $"{id}: id must look like {set}-001";
        }

        foreach (var field in new[] { "prompt", "language", "category" })
        {
            if (!task.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            {
                yield return $"{id}: '{field}' must be a non-empty string";
            }
        }

        var category = OptionalText(task, "category");
        var language = OptionalText(task, "language");
        if (!Categories.Contains(category))
        {
            yield return $"{id}: unknown category '{category}'";
        }

        if (language is not ("de" or "fr"))
        {
            yield return $"{id}: language must be de or fr";
        }

        if (category == "fr" && language != "fr")
        {
            yield return $"{id}: category fr needs language fr";
        }

        if (!task.TryGetProperty("smoke", out var smoke) || smoke.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            yield return $"{id}: 'smoke' must be a boolean";
        }

        if (!task.TryGetProperty("max_tool_calls", out var maxCalls) || !maxCalls.TryGetInt32(out var max) || max is < 1 or > 20)
        {
            yield return $"{id}: 'max_tool_calls' must be an integer between 1 and 20";
        }

        var capabilities = task.TryGetProperty("capabilities_required", out var caps) && caps.ValueKind == JsonValueKind.Array
            ? caps.EnumerateArray().Select(cap => cap.GetString() ?? "").ToList()
            : null;
        if (capabilities is null || capabilities.Count == 0)
        {
            yield return $"{id}: 'capabilities_required' must be a non-empty array";
        }
        else
        {
            foreach (var unknown in capabilities.Except(Capabilities))
            {
                yield return $"{id}: unknown capability '{unknown}'";
            }

            if (category == "extended" && !capabilities.Intersect(["occupancy", "disruptions"]).Any())
            {
                yield return $"{id}: extended tasks need capability occupancy or disruptions";
            }
        }

        if (!task.TryGetProperty("checks", out var checks) || checks.ValueKind != JsonValueKind.Array || checks.GetArrayLength() == 0)
        {
            yield return $"{id}: 'checks' must be a non-empty array";
            yield break;
        }

        foreach (var check in checks.EnumerateArray())
        {
            foreach (var violation in ValidateCheck(id, category, check))
            {
                yield return violation;
            }
        }
    }

    private static IEnumerable<string> ValidateCheck(string id, string category, JsonElement check)
    {
        switch (OptionalText(check, "type"))
        {
            case "regex":
                var pattern = OptionalText(check, "pattern");
                string? regexError = null;
                try
                {
                    _ = new Regex(pattern);
                }
                catch (ArgumentException)
                {
                    regexError = $"{id}: invalid regex";
                }

                if (string.IsNullOrWhiteSpace(pattern) || regexError is not null)
                {
                    yield return regexError ?? $"{id}: regex check needs a pattern";
                }

                // "\b" in JSON is a backspace, not a word boundary; a pattern with control characters never matches (task 4.3, test-002).
                if (pattern is not null && pattern.Any(char.IsControl))
                {
                    yield return $"{id}: regex pattern contains a control character (write \\\\b in JSON for a word boundary)";
                }

                break;

            case "judge":
                if (!JudgeCategories.Contains(category))
                {
                    yield return $"{id}: judge checks only for {string.Join("/", JudgeCategories)} (AK-9.2)";
                }

                if (string.IsNullOrWhiteSpace(OptionalText(check, "rubric")))
                {
                    yield return $"{id}: judge check needs a rubric";
                }

                break;

            case "oracle":
                foreach (var violation in ValidateOracle(id, check))
                {
                    yield return violation;
                }

                break;

            default:
                yield return $"{id}: unknown check type";
                break;
        }
    }

    private static IEnumerable<string> ValidateOracle(string id, JsonElement check)
    {
        var oracle = OptionalText(check, "oracle");
        if (!OracleRequiredParams.TryGetValue(oracle, out var required))
        {
            yield return $"{id}: unknown oracle '{oracle}'";
            yield break;
        }

        if (!check.TryGetProperty("tolerance_minutes", out var tolerance) || !tolerance.TryGetInt32(out var minutes) || minutes is < 0 or > 30)
        {
            yield return $"{id}: oracle check needs tolerance_minutes between 0 and 30";
        }

        if (!check.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object)
        {
            yield return $"{id}: oracle check needs params";
            yield break;
        }

        var names = parameters.EnumerateObject().Select(p => p.Name).ToList();
        foreach (var missing in required.Except(names))
        {
            yield return $"{id}: oracle {oracle} needs param '{missing}'";
        }

        foreach (var unknown in names.Except(required).Except(OracleOptionalParams[oracle]))
        {
            yield return $"{id}: oracle {oracle} does not take param '{unknown}'";
        }

        foreach (var timeParam in TimeParams.Where(names.Contains))
        {
            var value = OptionalText(parameters, timeParam);
            if (!TimeExpression().IsMatch(value))
            {
                yield return $"{id}: '{timeParam}' must be now, +<n>d HH:mm or next:<day> HH:mm";
            }
        }

        if (parameters.TryGetProperty("via", out var via)
            && (via.ValueKind != JsonValueKind.Array || via.GetArrayLength() is < 1 or > 2))
        {
            yield return $"{id}: 'via' must be an array of 1 or 2 stops";
        }

        if (oracle == "occupancy" && (!parameters.TryGetProperty("class", out var travelClass) || !travelClass.TryGetInt32(out var c) || c is not (1 or 2)))
        {
            yield return $"{id}: occupancy class must be 1 or 2";
        }
    }

    private static List<JsonElement> Load(string set)
    {
        var path = Path.Combine(Directory, $"{set}.jsonl");
        Assert.True(File.Exists(path), $"bench/tasks/{set}.jsonl is missing");

        return
        [
            .. File.ReadAllLines(path)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select((line, index) =>
                {
                    try
                    {
                        return JsonDocument.Parse(line).RootElement.Clone();
                    }
                    catch (JsonException)
                    {
                        Assert.Fail($"{set}.jsonl line {index + 1} is not valid JSON");
                        throw;
                    }
                }),
        ];
    }

    private static string Text(JsonElement element, string property) => element.GetProperty(property).GetString()!;

    private static string OptionalText(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    [GeneratedRegex(@"^(now|\+\d+d (?:[01]\d|2[0-3]):[0-5]\d|next:(?:mon|tue|wed|thu|fri|sat|sun) (?:[01]\d|2[0-3]):[0-5]\d)$")]
    private static partial Regex TimeExpression();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
