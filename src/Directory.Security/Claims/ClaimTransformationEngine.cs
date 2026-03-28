using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Directory.Security.Claims;

/// <summary>
/// Transformation rule types per MS-CTA section 2.2.
/// </summary>
public enum TransformationRuleType
{
    /// <summary>Allow matching claims to pass through.</summary>
    Allow,

    /// <summary>Deny (suppress) matching claims.</summary>
    Deny,

    /// <summary>Issue a new claim based on input claims.</summary>
    Issue
}

/// <summary>
/// Default action for claims that do not match any rule.
/// Per MS-CTA section 3.1, the default determines what happens to unmatched claims.
/// </summary>
public enum DefaultRuleAction
{
    /// <summary>All claims are allowed unless explicitly denied.</summary>
    AllowAll,

    /// <summary>All claims are denied unless explicitly allowed.</summary>
    DenyAllExcept,

    /// <summary>All claims are denied unconditionally.</summary>
    DenyAll
}

/// <summary>
/// A condition that can be evaluated against a claim entry.
/// </summary>
public class RuleCondition
{
    /// <summary>
    /// The operator for comparison (Equals, NotEquals, Contains, StartsWith, RegexMatch, Exists, etc.).
    /// </summary>
    public string Operator { get; set; } = "Equals";

    /// <summary>
    /// The value to compare against. Null for unary operators like Exists.
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    /// Whether the comparison is case-insensitive. Default true for string comparisons.
    /// </summary>
    public bool IgnoreCase { get; set; } = true;
}

/// <summary>
/// A single transformation rule per MS-CTA section 2.2.
/// </summary>
public class TransformationRule
{
    /// <summary>
    /// The rule type: Allow, Deny, or Issue.
    /// </summary>
    public TransformationRuleType Type { get; set; }

    /// <summary>
    /// The claim type to match on the input side (e.g., "ad://ext/department").
    /// Use "*" to match all claim types.
    /// </summary>
    public string SourceClaimType { get; set; } = "*";

    /// <summary>
    /// For Issue rules, the claim type to produce in the output.
    /// If null, the source claim type is preserved.
    /// </summary>
    public string TargetClaimType { get; set; }

    /// <summary>
    /// Optional condition that must be satisfied for the rule to apply.
    /// </summary>
    public RuleCondition Condition { get; set; }

    /// <summary>
    /// Optional value mapping: maps input values to output values.
    /// Key = source value, Value = target value. Used for Issue rules with value transformation.
    /// </summary>
    public Dictionary<string, string> ValueMapping { get; set; }
}

/// <summary>
/// A set of transformation rules with a default action per MS-CTA section 2.2.
/// </summary>
public class TransformationRuleSet
{
    /// <summary>
    /// The ordered list of rules to evaluate.
    /// </summary>
    public List<TransformationRule> Rules { get; set; } = [];

    /// <summary>
    /// The default action for claims that don't match any rule.
    /// </summary>
    public DefaultRuleAction DefaultAction { get; set; } = DefaultRuleAction.AllowAll;
}

/// <summary>
/// Evaluates claims transformation rules per MS-CTA section 3.1.
/// Parses rule definitions from XML and transforms <see cref="ClaimsSet"/> instances.
/// </summary>
public class ClaimTransformationEngine
{
    private readonly ILogger<ClaimTransformationEngine> _logger;

    public ClaimTransformationEngine(ILogger<ClaimTransformationEngine> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parses transformation rules from an XML rule definition.
    /// Per MS-CTA section 2.2, rules are stored in msDS-TransformationRules as XML.
    /// </summary>
    /// <remarks>
    /// Expected XML format:
    /// <code>
    /// &lt;ClaimsTransformationPolicy&gt;
    ///   &lt;DefaultAction&gt;AllowAll&lt;/DefaultAction&gt;
    ///   &lt;Rules&gt;
    ///     &lt;Rule Type="Allow" SourceClaimType="ad://ext/department"&gt;
    ///       &lt;Condition Operator="Equals" Value="Engineering" /&gt;
    ///     &lt;/Rule&gt;
    ///   &lt;/Rules&gt;
    /// &lt;/ClaimsTransformationPolicy&gt;
    /// </code>
    /// </remarks>
    public TransformationRuleSet ParseRules(string rulesXml)
    {
        var ruleSet = new TransformationRuleSet();

        if (string.IsNullOrWhiteSpace(rulesXml))
            return ruleSet;

        try
        {
            var doc = XDocument.Parse(rulesXml);
            var root = doc.Root;

            if (root is null)
                return ruleSet;

            // Parse default action
            var defaultActionStr = root.Element("DefaultAction")?.Value;
            if (!string.IsNullOrEmpty(defaultActionStr))
            {
                ruleSet.DefaultAction = defaultActionStr switch
                {
                    "DenyAllExcept" => DefaultRuleAction.DenyAllExcept,
                    "DenyAll" => DefaultRuleAction.DenyAll,
                    _ => DefaultRuleAction.AllowAll
                };
            }

            // Parse individual rules
            var rulesElement = root.Element("Rules");
            if (rulesElement is null)
                return ruleSet;

            foreach (var ruleElement in rulesElement.Elements("Rule"))
            {
                var rule = ParseRuleElement(ruleElement);
                if (rule is not null)
                    ruleSet.Rules.Add(rule);
            }

            _logger.LogDebug("Parsed {Count} transformation rules with default action {Default}",
                ruleSet.Rules.Count, ruleSet.DefaultAction);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse claims transformation rules XML");
        }

        return ruleSet;
    }

    /// <summary>
    /// Transforms a claims set through a set of rules per MS-CTA section 3.1.
    /// </summary>
    public ClaimsSet TransformClaims(ClaimsSet input, TransformationRuleSet rules)
    {
        var output = new ClaimsSet();

        foreach (var inputArray in input.ClaimsArrays)
        {
            var outputArray = new ClaimsArray
            {
                ClaimSourceType = inputArray.ClaimSourceType
            };

            foreach (var claim in inputArray.ClaimEntries)
            {
                var transformedClaims = TransformSingleClaim(claim, rules);
                outputArray.ClaimEntries.AddRange(transformedClaims);
            }

            if (outputArray.ClaimEntries.Count > 0)
                output.ClaimsArrays.Add(outputArray);
        }

        return output;
    }

    /// <summary>
    /// Evaluates a condition against a claim entry.
    /// </summary>
    public bool EvaluateCondition(ClaimEntry claim, RuleCondition condition)
    {
        if (claim.Type == ClaimValueType.STRING && claim.StringValues is not null)
        {
            return condition.Operator switch
            {
                "Equals" => claim.StringValues.Any(v =>
                    string.Equals(v, condition.Value,
                        condition.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)),

                "NotEquals" => claim.StringValues.All(v =>
                    !string.Equals(v, condition.Value,
                        condition.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)),

                "Contains" => condition.Value is not null && claim.StringValues.Any(v =>
                    v.Contains(condition.Value,
                        condition.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)),

                "StartsWith" => condition.Value is not null && claim.StringValues.Any(v =>
                    v.StartsWith(condition.Value,
                        condition.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)),

                "EndsWith" => condition.Value is not null && claim.StringValues.Any(v =>
                    v.EndsWith(condition.Value,
                        condition.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)),

                "RegexMatch" => condition.Value is not null && claim.StringValues.Any(v =>
                    System.Text.RegularExpressions.Regex.IsMatch(v, condition.Value,
                        condition.IgnoreCase
                            ? System.Text.RegularExpressions.RegexOptions.IgnoreCase
                            : System.Text.RegularExpressions.RegexOptions.None)),

                "Exists" => claim.StringValues.Count > 0,

                _ => false
            };
        }

        if (claim.Type == ClaimValueType.INT64 && claim.Int64Values is not null)
        {
            if (condition.Operator == "Exists")
                return claim.Int64Values.Count > 0;

            if (condition.Value is null || !long.TryParse(condition.Value, out var compareValue))
                return false;

            return condition.Operator switch
            {
                "Equals" => claim.Int64Values.Any(v => v == compareValue),
                "NotEquals" => claim.Int64Values.All(v => v != compareValue),
                "GreaterThan" => claim.Int64Values.Any(v => v > compareValue),
                "LessThan" => claim.Int64Values.Any(v => v < compareValue),
                _ => false
            };
        }

        if (claim.Type == ClaimValueType.UINT64 && claim.UInt64Values is not null)
        {
            if (condition.Operator == "Exists")
                return claim.UInt64Values.Count > 0;

            if (condition.Value is null || !ulong.TryParse(condition.Value, out var compareValue))
                return false;

            return condition.Operator switch
            {
                "Equals" => claim.UInt64Values.Any(v => v == compareValue),
                "NotEquals" => claim.UInt64Values.All(v => v != compareValue),
                _ => false
            };
        }

        if (claim.Type == ClaimValueType.BOOLEAN && claim.BooleanValues is not null)
        {
            if (condition.Operator == "Exists")
                return claim.BooleanValues.Count > 0;

            if (condition.Value is null || !bool.TryParse(condition.Value, out var compareValue))
                return false;

            return condition.Operator switch
            {
                "Equals" => claim.BooleanValues.Any(v => v == compareValue),
                "NotEquals" => claim.BooleanValues.All(v => v != compareValue),
                _ => false
            };
        }

        // SID type: only Exists is meaningful
        if (claim.Type == ClaimValueType.SID && claim.SidValues is not null)
        {
            return condition.Operator == "Exists" && claim.SidValues.Count > 0;
        }

        return false;
    }

    /// <summary>
    /// Transforms a single claim entry through the rule set.
    /// Returns zero or more output claim entries.
    /// </summary>
    private List<ClaimEntry> TransformSingleClaim(ClaimEntry claim, TransformationRuleSet rules)
    {
        var results = new List<ClaimEntry>();
        bool matchedAnyRule = false;

        foreach (var rule in rules.Rules)
        {
            // Check if rule applies to this claim type
            if (!MatchesClaimType(claim.Id, rule.SourceClaimType))
                continue;

            // Check condition if present
            if (rule.Condition is not null && !EvaluateCondition(claim, rule.Condition))
                continue;

            matchedAnyRule = true;

            switch (rule.Type)
            {
                case TransformationRuleType.Allow:
                    // Pass through the claim (possibly with type rename)
                    var allowed = claim.Clone();
                    if (rule.TargetClaimType is not null)
                        allowed.Id = rule.TargetClaimType;
                    results.Add(allowed);
                    return results; // First matching Allow wins

                case TransformationRuleType.Deny:
                    // Suppress this claim
                    _logger.LogDebug("Claim {ClaimId} denied by transformation rule", claim.Id);
                    return results; // Return empty - claim is denied

                case TransformationRuleType.Issue:
                    // Issue a new/transformed claim
                    var issued = CreateIssuedClaim(claim, rule);
                    if (issued is not null)
                        results.Add(issued);
                    return results;
            }
        }

        // No rule matched - apply default action
        if (!matchedAnyRule)
        {
            switch (rules.DefaultAction)
            {
                case DefaultRuleAction.AllowAll:
                    results.Add(claim.Clone());
                    break;

                case DefaultRuleAction.DenyAllExcept:
                case DefaultRuleAction.DenyAll:
                    _logger.LogDebug("Claim {ClaimId} dropped by default {Action} policy",
                        claim.Id, rules.DefaultAction);
                    break;
            }
        }

        return results;
    }

    /// <summary>
    /// Creates an issued claim from an input claim and an Issue rule.
    /// Applies value mapping and type renaming as specified.
    /// </summary>
    private static ClaimEntry CreateIssuedClaim(ClaimEntry source, TransformationRule rule)
    {
        var issued = source.Clone();
        issued.Id = rule.TargetClaimType ?? source.Id;

        // Apply value mapping if present
        if (rule.ValueMapping is not null && source.Type == ClaimValueType.STRING && issued.StringValues is not null)
        {
            var mappedValues = new List<string>();
            foreach (var val in issued.StringValues)
            {
                if (rule.ValueMapping.TryGetValue(val, out var mappedValue))
                    mappedValues.Add(mappedValue);
                // Values not in the mapping are dropped
            }

            if (mappedValues.Count == 0)
                return null;

            issued.StringValues = mappedValues;
        }

        return issued;
    }

    /// <summary>
    /// Checks whether a claim ID matches a rule's source claim type pattern.
    /// Supports "*" wildcard to match all claim types.
    /// </summary>
    private static bool MatchesClaimType(string claimId, string ruleClaimType)
    {
        if (ruleClaimType == "*")
            return true;

        return string.Equals(claimId, ruleClaimType, StringComparison.OrdinalIgnoreCase);
    }

    private TransformationRule ParseRuleElement(XElement element)
    {
        var typeStr = element.Attribute("Type")?.Value;
        if (string.IsNullOrEmpty(typeStr))
            return null;

        var rule = new TransformationRule
        {
            Type = typeStr switch
            {
                "Allow" => TransformationRuleType.Allow,
                "Deny" => TransformationRuleType.Deny,
                "Issue" => TransformationRuleType.Issue,
                _ => TransformationRuleType.Allow
            },
            SourceClaimType = element.Attribute("SourceClaimType")?.Value ?? "*",
            TargetClaimType = element.Attribute("TargetClaimType")?.Value
        };

        // Parse condition
        var conditionElement = element.Element("Condition");
        if (conditionElement is not null)
        {
            rule.Condition = new RuleCondition
            {
                Operator = conditionElement.Attribute("Operator")?.Value ?? "Equals",
                Value = conditionElement.Attribute("Value")?.Value,
                IgnoreCase = !string.Equals(
                    conditionElement.Attribute("IgnoreCase")?.Value, "false", StringComparison.OrdinalIgnoreCase)
            };
        }

        // Parse value mappings
        var mappingsElement = element.Element("ValueMappings");
        if (mappingsElement is not null)
        {
            rule.ValueMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mapping in mappingsElement.Elements("Map"))
            {
                var from = mapping.Attribute("From")?.Value;
                var to = mapping.Attribute("To")?.Value;
                if (from is not null && to is not null)
                    rule.ValueMapping[from] = to;
            }
        }

        return rule;
    }
}
