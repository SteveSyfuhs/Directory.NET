using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Security.Claims;

/// <summary>
/// A central access rule defines a resource condition and security descriptor
/// that together control access based on claims. Per MS-ADTS section 5.1.3.4.
/// </summary>
public class CentralAccessRule
{
    /// <summary>
    /// The display name of the rule (from msDS-CentralAccessRule CN).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The resource condition expression (from msDS-ResourceCondition) that determines
    /// whether this rule applies to a given resource. Uses conditional ACE expression syntax.
    /// Null means the rule applies to all resources.
    /// </summary>
    public string ResourceCondition { get; set; }

    /// <summary>
    /// The proposed security descriptor for evaluating access.
    /// This SD contains the permission ACEs with claim-based conditions.
    /// </summary>
    public SecurityDescriptor SecurityDescriptor { get; set; }

    /// <summary>
    /// The effective security descriptor currently in use (msDS-MembersOfResourcePropertyList).
    /// Falls back to <see cref="SecurityDescriptor"/> if not set.
    /// </summary>
    public SecurityDescriptor EffectiveSecurityDescriptor { get; set; }
}

/// <summary>
/// A central access policy groups multiple <see cref="CentralAccessRule"/> entries.
/// Per MS-ADTS section 5.1.3.4, a CAP is referenced from resources via msDS-MembersOfResourcePropertyList.
/// </summary>
public class CentralAccessPolicy
{
    /// <summary>
    /// The display name of the policy.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The ordered list of access rules in this policy.
    /// </summary>
    public List<CentralAccessRule> Rules { get; set; } = [];
}

/// <summary>
/// Token types for the conditional ACE expression RPN evaluator.
/// Per MS-DTYP section 2.4.4.17.
/// </summary>
public enum ConditionalAceTokenType
{
    /// <summary>Literal integer value.</summary>
    LiteralInt,

    /// <summary>Literal string value.</summary>
    LiteralString,

    /// <summary>Literal SID value.</summary>
    LiteralSid,

    /// <summary>Local attribute (claim) reference.</summary>
    LocalAttribute,

    /// <summary>User attribute (claim) reference.</summary>
    UserAttribute,

    /// <summary>Device attribute (claim) reference.</summary>
    DeviceAttribute,

    /// <summary>Resource attribute reference.</summary>
    ResourceAttribute,

    /// <summary>Equality operator (==).</summary>
    Equals,

    /// <summary>Inequality operator (!=).</summary>
    NotEquals,

    /// <summary>Less than operator.</summary>
    LessThan,

    /// <summary>Less than or equal operator.</summary>
    LessThanOrEqual,

    /// <summary>Greater than operator.</summary>
    GreaterThan,

    /// <summary>Greater than or equal operator.</summary>
    GreaterThanOrEqual,

    /// <summary>Member_of operator - checks SID membership.</summary>
    MemberOf,

    /// <summary>Device_Member_of operator.</summary>
    DeviceMemberOf,

    /// <summary>Contains operator.</summary>
    Contains,

    /// <summary>Any_of operator.</summary>
    AnyOf,

    /// <summary>Logical NOT.</summary>
    Not,

    /// <summary>Logical AND.</summary>
    And,

    /// <summary>Logical OR.</summary>
    Or,

    /// <summary>Exists unary operator.</summary>
    Exists,

    /// <summary>Not_Exists unary operator.</summary>
    NotExists,

    /// <summary>Composite literal (set of values).</summary>
    Composite
}

/// <summary>
/// A token in a conditional ACE expression (RPN-encoded).
/// </summary>
public class ConditionalAceToken
{
    public ConditionalAceTokenType Type { get; set; }
    public string StringValue { get; set; }
    public long? IntValue { get; set; }
    public byte[] SidValue { get; set; }
    public List<ConditionalAceToken> CompositeValues { get; set; }
}

/// <summary>
/// A parsed conditional ACE expression, represented as an RPN token list.
/// Per MS-DTYP section 2.4.4.17, conditional ACE expressions use
/// a reverse-Polish notation byte encoding.
/// </summary>
public class ConditionalAceExpression
{
    /// <summary>
    /// The RPN token list for this expression.
    /// </summary>
    public List<ConditionalAceToken> Tokens { get; set; } = [];
}

/// <summary>
/// Evaluates Dynamic Access Control (DAC) policies, including claim-based conditional ACEs
/// and central access policies, per MS-ADTS section 5.1.3.4 and MS-DTYP section 2.4.4.17.
/// </summary>
public class DynamicAccessControlEvaluator
{
    private readonly ILogger<DynamicAccessControlEvaluator> _logger;

    public DynamicAccessControlEvaluator(ILogger<DynamicAccessControlEvaluator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Evaluates whether the given user (and optionally device) claims grant the desired access
    /// against a security descriptor that may contain conditional ACEs.
    /// </summary>
    /// <param name="userClaims">The user's claims set.</param>
    /// <param name="deviceClaims">The device's claims set, or null if no device claims.</param>
    /// <param name="sd">The security descriptor to evaluate against.</param>
    /// <param name="desiredAccess">The requested access mask.</param>
    /// <returns>True if access is granted.</returns>
    public bool EvaluateAccess(
        ClaimsSet userClaims,
        ClaimsSet deviceClaims,
        SecurityDescriptor sd,
        uint desiredAccess)
    {
        if (sd.Dacl is null)
        {
            _logger.LogDebug("No DACL present, granting access");
            return true;
        }

        uint denied = 0;
        uint allowed = 0;

        foreach (var ace in sd.Dacl.Aces)
        {
            if ((ace.Flags & AceFlags.InheritOnly) != 0)
                continue;

            // Check if this is a conditional ACE (has an embedded expression)
            // Conditional ACEs are identified by having a callback ACE type
            // For our model, we check for conditions stored as ObjectType-based references
            bool aceApplies = true;

            // Evaluate conditional expression if present
            var conditionBytes = GetConditionalAceData(ace);
            if (conditionBytes is not null)
            {
                var expression = ParseConditionalAceExpression(conditionBytes);
                if (expression is not null)
                {
                    aceApplies = EvaluateConditionalAce(expression, userClaims, deviceClaims);
                }
            }

            if (!aceApplies)
                continue;

            switch (ace.Type)
            {
                case AceType.AccessDenied:
                case AceType.AccessDeniedObject:
                    denied |= (uint)ace.Mask;
                    break;
                case AceType.AccessAllowed:
                case AceType.AccessAllowedObject:
                    allowed |= (uint)ace.Mask;
                    break;
            }
        }

        uint effective = allowed & ~denied;
        bool result = (effective & desiredAccess) == desiredAccess;

        if (!result)
        {
            _logger.LogDebug("DAC access check failed: desired=0x{Desired:X}, effective=0x{Effective:X}",
                desiredAccess, effective);
        }

        return result;
    }

    /// <summary>
    /// Evaluates a conditional ACE expression against the provided claims.
    /// Uses an RPN stack-based evaluator.
    /// </summary>
    public bool EvaluateConditionalAce(ConditionalAceExpression expr, ClaimsSet claims)
    {
        return EvaluateConditionalAce(expr, claims, null);
    }

    /// <summary>
    /// Evaluates a conditional ACE expression against user and device claims.
    /// Per MS-DTYP section 2.4.4.17, the expression is in RPN format.
    /// </summary>
    public bool EvaluateConditionalAce(
        ConditionalAceExpression expr,
        ClaimsSet userClaims,
        ClaimsSet deviceClaims)
    {
        var stack = new Stack<object>();

        foreach (var token in expr.Tokens)
        {
            switch (token.Type)
            {
                case ConditionalAceTokenType.LiteralInt:
                    stack.Push(token.IntValue ?? 0L);
                    break;

                case ConditionalAceTokenType.LiteralString:
                    stack.Push(token.StringValue ?? string.Empty);
                    break;

                case ConditionalAceTokenType.LiteralSid:
                    stack.Push(token.SidValue);
                    break;

                case ConditionalAceTokenType.Composite:
                    stack.Push(token.CompositeValues ?? new List<ConditionalAceToken>());
                    break;

                case ConditionalAceTokenType.LocalAttribute:
                case ConditionalAceTokenType.UserAttribute:
                    stack.Push(ResolveClaim(userClaims, token.StringValue));
                    break;

                case ConditionalAceTokenType.DeviceAttribute:
                    stack.Push(deviceClaims is not null
                        ? ResolveClaim(deviceClaims, token.StringValue)
                        : null);
                    break;

                case ConditionalAceTokenType.ResourceAttribute:
                    // Resource attributes would be resolved from the resource's SD
                    stack.Push(null);
                    break;

                case ConditionalAceTokenType.Equals:
                    EvaluateBinaryOp(stack, (a, b) => CompareValues(a, b) == 0);
                    break;

                case ConditionalAceTokenType.NotEquals:
                    EvaluateBinaryOp(stack, (a, b) => CompareValues(a, b) != 0);
                    break;

                case ConditionalAceTokenType.LessThan:
                    EvaluateBinaryOp(stack, (a, b) => CompareValues(a, b) < 0);
                    break;

                case ConditionalAceTokenType.LessThanOrEqual:
                    EvaluateBinaryOp(stack, (a, b) => CompareValues(a, b) <= 0);
                    break;

                case ConditionalAceTokenType.GreaterThan:
                    EvaluateBinaryOp(stack, (a, b) => CompareValues(a, b) > 0);
                    break;

                case ConditionalAceTokenType.GreaterThanOrEqual:
                    EvaluateBinaryOp(stack, (a, b) => CompareValues(a, b) >= 0);
                    break;

                case ConditionalAceTokenType.Contains:
                    EvaluateContainsOp(stack, allRequired: true);
                    break;

                case ConditionalAceTokenType.AnyOf:
                    EvaluateContainsOp(stack, allRequired: false);
                    break;

                case ConditionalAceTokenType.Not:
                    if (stack.Count >= 1)
                    {
                        var val = stack.Pop();
                        stack.Push(val is bool b ? !b : (object)null);
                    }
                    break;

                case ConditionalAceTokenType.And:
                    EvaluateBinaryOp(stack, (a, b) =>
                    {
                        if (a is bool ab && b is bool bb)
                            return ab && bb;
                        return false;
                    });
                    break;

                case ConditionalAceTokenType.Or:
                    EvaluateBinaryOp(stack, (a, b) =>
                    {
                        if (a is bool ab && b is bool bb)
                            return ab || bb;
                        return false;
                    });
                    break;

                case ConditionalAceTokenType.Exists:
                    if (stack.Count >= 1)
                    {
                        var val = stack.Pop();
                        stack.Push(val is not null);
                    }
                    break;

                case ConditionalAceTokenType.NotExists:
                    if (stack.Count >= 1)
                    {
                        var val = stack.Pop();
                        stack.Push(val is null);
                    }
                    break;

                case ConditionalAceTokenType.MemberOf:
                case ConditionalAceTokenType.DeviceMemberOf:
                    // For MemberOf, pop SID set and check membership
                    // Simplified: push false as we don't have group membership resolution here
                    if (stack.Count >= 2) { stack.Pop(); stack.Pop(); }
                    stack.Push(false);
                    break;
            }
        }

        if (stack.Count == 1 && stack.Pop() is bool result)
            return result;

        _logger.LogDebug("Conditional ACE expression evaluation did not produce a boolean result");
        return false;
    }

    /// <summary>
    /// Evaluates access for a resource protected by a central access policy.
    /// Each rule in the CAP is evaluated independently; all must grant access.
    /// </summary>
    public bool EvaluateCentralAccessPolicy(
        ClaimsSet userClaims,
        ClaimsSet deviceClaims,
        CentralAccessPolicy policy,
        uint desiredAccess)
    {
        foreach (var rule in policy.Rules)
        {
            // Check if the resource condition applies (if specified)
            if (rule.ResourceCondition is not null)
            {
                var conditionExpr = ParseConditionalAceString(rule.ResourceCondition);
                if (conditionExpr is not null && !EvaluateConditionalAce(conditionExpr, userClaims, deviceClaims))
                {
                    // Resource condition not met, rule does not apply - skip it
                    continue;
                }
            }

            var sd = rule.EffectiveSecurityDescriptor ?? rule.SecurityDescriptor;
            if (sd is null)
                continue;

            if (!EvaluateAccess(userClaims, deviceClaims, sd, desiredAccess))
            {
                _logger.LogDebug("Central access rule {Rule} denied access", rule.Name);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Parses a conditional ACE expression from a human-readable string format.
    /// Supports basic expressions like:
    ///   @User.department == "Engineering"
    ///   @User.title == "Manager" &amp;&amp; @Device.operatingSystem == "Windows"
    ///   Exists @User.department
    /// </summary>
    public ConditionalAceExpression ParseConditionalAceString(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return null;

        try
        {
            var tokens = new List<ConditionalAceToken>();
            var parts = TokenizeExpression(expression);
            var outputQueue = new Queue<ConditionalAceToken>();
            var operatorStack = new Stack<ConditionalAceToken>();

            foreach (var part in parts)
            {
                if (part.Type == ConditionalAceTokenType.LiteralString
                    || part.Type == ConditionalAceTokenType.LiteralInt
                    || part.Type == ConditionalAceTokenType.UserAttribute
                    || part.Type == ConditionalAceTokenType.DeviceAttribute
                    || part.Type == ConditionalAceTokenType.ResourceAttribute
                    || part.Type == ConditionalAceTokenType.LocalAttribute)
                {
                    outputQueue.Enqueue(part);
                }
                else
                {
                    // Operator: flush higher/equal precedence operators
                    int prec = GetPrecedence(part.Type);
                    while (operatorStack.Count > 0 && GetPrecedence(operatorStack.Peek().Type) >= prec)
                    {
                        outputQueue.Enqueue(operatorStack.Pop());
                    }
                    operatorStack.Push(part);
                }
            }

            while (operatorStack.Count > 0)
                outputQueue.Enqueue(operatorStack.Pop());

            return new ConditionalAceExpression { Tokens = [.. outputQueue] };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse conditional ACE expression: {Expression}", expression);
            return null;
        }
    }

    /// <summary>
    /// Parses a conditional ACE expression from the binary format stored in ACE callback data.
    /// Per MS-DTYP section 2.4.4.17.1, the first 4 bytes are the signature "artx".
    /// </summary>
    public ConditionalAceExpression ParseConditionalAceExpression(byte[] data)
    {
        if (data.Length < 4)
            return null;

        // Check for "artx" signature
        if (data[0] != 0x61 || data[1] != 0x72 || data[2] != 0x74 || data[3] != 0x78)
        {
            _logger.LogDebug("Conditional ACE data does not have 'artx' signature");
            return null;
        }

        var expr = new ConditionalAceExpression();
        int offset = 4;

        while (offset < data.Length)
        {
            byte tokenByte = data[offset++];
            var token = ParseBinaryToken(data, tokenByte, ref offset);
            if (token is not null)
                expr.Tokens.Add(token);
        }

        return expr;
    }

    private static byte[] GetConditionalAceData(AccessControlEntry ace)
    {
        // In a real implementation, conditional ACE data would be stored
        // in the ACE body after the SID. For our model, we use the
        // Attributes dictionary or a specific marker. Return null for
        // standard (non-callback) ACE types.
        return null;
    }

    private static ConditionalAceToken ParseBinaryToken(byte[] data, byte tokenByte, ref int offset)
    {
        // Per MS-DTYP 2.4.4.17.1 token opcodes
        return tokenByte switch
        {
            // Literal integer (INT64)
            0x01 or 0x02 or 0x03 or 0x04 => ParseBinaryIntToken(data, ref offset),

            // Literal string
            0x10 => ParseBinaryStringToken(data, ref offset),

            // Local attribute
            0x50 => ParseAttributeToken(data, ref offset, ConditionalAceTokenType.LocalAttribute),

            // User attribute
            0x51 => ParseAttributeToken(data, ref offset, ConditionalAceTokenType.UserAttribute),

            // Resource attribute
            0x52 => ParseAttributeToken(data, ref offset, ConditionalAceTokenType.ResourceAttribute),

            // Device attribute
            0x53 => ParseAttributeToken(data, ref offset, ConditionalAceTokenType.DeviceAttribute),

            // Operators
            0x80 => new ConditionalAceToken { Type = ConditionalAceTokenType.Equals },
            0x81 => new ConditionalAceToken { Type = ConditionalAceTokenType.NotEquals },
            0x82 => new ConditionalAceToken { Type = ConditionalAceTokenType.LessThan },
            0x83 => new ConditionalAceToken { Type = ConditionalAceTokenType.LessThanOrEqual },
            0x84 => new ConditionalAceToken { Type = ConditionalAceTokenType.GreaterThan },
            0x85 => new ConditionalAceToken { Type = ConditionalAceTokenType.GreaterThanOrEqual },
            0x86 => new ConditionalAceToken { Type = ConditionalAceTokenType.Contains },
            0x87 => new ConditionalAceToken { Type = ConditionalAceTokenType.AnyOf },
            0x88 => new ConditionalAceToken { Type = ConditionalAceTokenType.MemberOf },
            0x89 => new ConditionalAceToken { Type = ConditionalAceTokenType.DeviceMemberOf },

            // Logical
            0xa0 => new ConditionalAceToken { Type = ConditionalAceTokenType.And },
            0xa1 => new ConditionalAceToken { Type = ConditionalAceTokenType.Or },
            0xa2 => new ConditionalAceToken { Type = ConditionalAceTokenType.Not },
            0xa3 => new ConditionalAceToken { Type = ConditionalAceTokenType.Exists },
            0xa4 => new ConditionalAceToken { Type = ConditionalAceTokenType.NotExists },

            // Padding / end
            0x00 => null,

            _ => null
        };
    }

    private static ConditionalAceToken ParseBinaryIntToken(byte[] data, ref int offset)
    {
        if (offset + 8 > data.Length)
            return null;

        long value = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(offset));
        offset += 8;
        // Skip sign and base bytes (3 bytes per MS-DTYP)
        offset += 3;

        return new ConditionalAceToken
        {
            Type = ConditionalAceTokenType.LiteralInt,
            IntValue = value
        };
    }

    private static ConditionalAceToken ParseBinaryStringToken(byte[] data, ref int offset)
    {
        if (offset + 4 > data.Length)
            return null;

        uint byteLen = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
        offset += 4;

        if (offset + (int)byteLen > data.Length)
            return null;

        string value = System.Text.Encoding.Unicode.GetString(data, offset, (int)byteLen);
        offset += (int)byteLen;

        return new ConditionalAceToken
        {
            Type = ConditionalAceTokenType.LiteralString,
            StringValue = value
        };
    }

    private static ConditionalAceToken ParseAttributeToken(byte[] data, ref int offset, ConditionalAceTokenType type)
    {
        if (offset + 4 > data.Length)
            return null;

        uint byteLen = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
        offset += 4;

        if (offset + (int)byteLen > data.Length)
            return null;

        string name = System.Text.Encoding.Unicode.GetString(data, offset, (int)byteLen);
        offset += (int)byteLen;

        return new ConditionalAceToken
        {
            Type = type,
            StringValue = name
        };
    }

    /// <summary>
    /// Resolves a claim attribute reference from a claims set.
    /// Returns the first string value if found, otherwise null.
    /// </summary>
    private static object ResolveClaim(ClaimsSet claims, string attributeName)
    {
        if (attributeName is null)
            return null;

        var claim = claims.FindClaim(attributeName);
        if (claim is null)
        {
            // Also try with ad://ext/ prefix
            claim = claims.FindClaim($"ad://ext/{attributeName}");
        }

        if (claim is null)
            return null;

        return claim.Type switch
        {
            ClaimValueType.STRING => claim.StringValues?.FirstOrDefault(),
            ClaimValueType.INT64 => claim.Int64Values?.FirstOrDefault(),
            ClaimValueType.UINT64 => (object)(claim.UInt64Values?.FirstOrDefault()),
            ClaimValueType.BOOLEAN => claim.BooleanValues?.FirstOrDefault(),
            _ => null
        };
    }

    private static int CompareValues(object a, object b)
    {
        if (a is null && b is null) return 0;
        if (a is null) return -1;
        if (b is null) return 1;

        // String comparison
        if (a is string sa && b is string sb)
            return string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);

        // Numeric comparison
        if (TryGetLong(a, out var la) && TryGetLong(b, out var lb))
            return la.CompareTo(lb);

        // Boolean comparison
        if (a is bool ba && b is bool bb)
            return ba.CompareTo(bb);

        // Fall back to string comparison
        return string.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetLong(object value, out long result)
    {
        switch (value)
        {
            case long l: result = l; return true;
            case ulong u when u <= (ulong)long.MaxValue: result = (long)u; return true;
            case int i: result = i; return true;
            default: result = 0; return false;
        }
    }

    private static void EvaluateBinaryOp(Stack<object> stack, Func<object, object, object> op)
    {
        if (stack.Count < 2)
        {
            stack.Push(null);
            return;
        }

        var right = stack.Pop();
        var left = stack.Pop();
        stack.Push(op(left, right));
    }

    private static void EvaluateContainsOp(Stack<object> stack, bool allRequired)
    {
        if (stack.Count < 2)
        {
            stack.Push(false);
            return;
        }

        var right = stack.Pop();
        var left = stack.Pop();

        // Left should be the claim value(s), right should be the test value(s)
        if (left is null)
        {
            stack.Push(false);
            return;
        }

        var leftStr = left.ToString();
        if (right is List<ConditionalAceToken> composite)
        {
            if (allRequired)
            {
                stack.Push(composite.All(t =>
                    string.Equals(leftStr, t.StringValue, StringComparison.OrdinalIgnoreCase)));
            }
            else
            {
                stack.Push(composite.Any(t =>
                    string.Equals(leftStr, t.StringValue, StringComparison.OrdinalIgnoreCase)));
            }
        }
        else
        {
            stack.Push(string.Equals(leftStr, right?.ToString(), StringComparison.OrdinalIgnoreCase));
        }
    }

    private static int GetPrecedence(ConditionalAceTokenType type)
    {
        return type switch
        {
            ConditionalAceTokenType.Or => 1,
            ConditionalAceTokenType.And => 2,
            ConditionalAceTokenType.Not => 5,
            ConditionalAceTokenType.Exists => 5,
            ConditionalAceTokenType.NotExists => 5,
            ConditionalAceTokenType.Equals => 3,
            ConditionalAceTokenType.NotEquals => 3,
            ConditionalAceTokenType.LessThan => 3,
            ConditionalAceTokenType.LessThanOrEqual => 3,
            ConditionalAceTokenType.GreaterThan => 3,
            ConditionalAceTokenType.GreaterThanOrEqual => 3,
            ConditionalAceTokenType.Contains => 4,
            ConditionalAceTokenType.AnyOf => 4,
            ConditionalAceTokenType.MemberOf => 4,
            ConditionalAceTokenType.DeviceMemberOf => 4,
            _ => 0
        };
    }

    /// <summary>
    /// Tokenizes a human-readable conditional ACE expression string into tokens.
    /// Supports: @User.attr, @Device.attr, @Resource.attr, ==, !=, &lt;, &lt;=, &gt;, &gt;=,
    /// &amp;&amp;, ||, !, "string literals", integer literals, Exists, Not_Exists, Contains, Any_of.
    /// </summary>
    private static List<ConditionalAceToken> TokenizeExpression(string expression)
    {
        var tokens = new List<ConditionalAceToken>();
        int i = 0;

        while (i < expression.Length)
        {
            // Skip whitespace
            if (char.IsWhiteSpace(expression[i]))
            {
                i++;
                continue;
            }

            // Attribute references: @User.xxx, @Device.xxx, @Resource.xxx
            if (expression[i] == '@')
            {
                i++;
                var refStr = ReadIdentifier(expression, ref i);

                if (refStr.StartsWith("User.", StringComparison.OrdinalIgnoreCase))
                {
                    tokens.Add(new ConditionalAceToken
                    {
                        Type = ConditionalAceTokenType.UserAttribute,
                        StringValue = refStr[5..]
                    });
                }
                else if (refStr.StartsWith("Device.", StringComparison.OrdinalIgnoreCase))
                {
                    tokens.Add(new ConditionalAceToken
                    {
                        Type = ConditionalAceTokenType.DeviceAttribute,
                        StringValue = refStr[7..]
                    });
                }
                else if (refStr.StartsWith("Resource.", StringComparison.OrdinalIgnoreCase))
                {
                    tokens.Add(new ConditionalAceToken
                    {
                        Type = ConditionalAceTokenType.ResourceAttribute,
                        StringValue = refStr[9..]
                    });
                }
                else
                {
                    tokens.Add(new ConditionalAceToken
                    {
                        Type = ConditionalAceTokenType.LocalAttribute,
                        StringValue = refStr
                    });
                }
                continue;
            }

            // String literal
            if (expression[i] == '"')
            {
                i++;
                var sb = new System.Text.StringBuilder();
                while (i < expression.Length && expression[i] != '"')
                {
                    if (expression[i] == '\\' && i + 1 < expression.Length)
                    {
                        i++;
                        sb.Append(expression[i]);
                    }
                    else
                    {
                        sb.Append(expression[i]);
                    }
                    i++;
                }
                if (i < expression.Length) i++; // skip closing quote
                tokens.Add(new ConditionalAceToken
                {
                    Type = ConditionalAceTokenType.LiteralString,
                    StringValue = sb.ToString()
                });
                continue;
            }

            // Operators
            if (i + 1 < expression.Length)
            {
                var twoChar = expression.Substring(i, 2);
                switch (twoChar)
                {
                    case "==":
                        tokens.Add(new ConditionalAceToken { Type = ConditionalAceTokenType.Equals });
                        i += 2;
                        continue;
                    case "!=":
                        tokens.Add(new ConditionalAceToken { Type = ConditionalAceTokenType.NotEquals });
                        i += 2;
                        continue;
                    case "<=":
                        tokens.Add(new ConditionalAceToken { Type = ConditionalAceTokenType.LessThanOrEqual });
                        i += 2;
                        continue;
                    case ">=":
                        tokens.Add(new ConditionalAceToken { Type = ConditionalAceTokenType.GreaterThanOrEqual });
                        i += 2;
                        continue;
                    case "&&":
                        tokens.Add(new ConditionalAceToken { Type = ConditionalAceTokenType.And });
                        i += 2;
                        continue;
                    case "||":
                        tokens.Add(new ConditionalAceToken { Type = ConditionalAceTokenType.Or });
                        i += 2;
                        continue;
                }
            }

            if (expression[i] == '<')
            {
                tokens.Add(new ConditionalAceToken { Type = ConditionalAceTokenType.LessThan });
                i++;
                continue;
            }

            if (expression[i] == '>')
            {
                tokens.Add(new ConditionalAceToken { Type = ConditionalAceTokenType.GreaterThan });
                i++;
                continue;
            }

            if (expression[i] == '!')
            {
                tokens.Add(new ConditionalAceToken { Type = ConditionalAceTokenType.Not });
                i++;
                continue;
            }

            // Number literal
            if (char.IsDigit(expression[i]) || (expression[i] == '-' && i + 1 < expression.Length && char.IsDigit(expression[i + 1])))
            {
                int start = i;
                if (expression[i] == '-') i++;
                while (i < expression.Length && char.IsDigit(expression[i]))
                    i++;

                if (long.TryParse(expression[start..i], out var num))
                {
                    tokens.Add(new ConditionalAceToken
                    {
                        Type = ConditionalAceTokenType.LiteralInt,
                        IntValue = num
                    });
                }
                continue;
            }

            // Keywords
            if (char.IsLetter(expression[i]) || expression[i] == '_')
            {
                var keyword = ReadIdentifier(expression, ref i);

                switch (keyword.ToLowerInvariant())
                {
                    case "exists":
                        tokens.Add(new ConditionalAceToken { Type = ConditionalAceTokenType.Exists });
                        break;
                    case "not_exists":
                        tokens.Add(new ConditionalAceToken { Type = ConditionalAceTokenType.NotExists });
                        break;
                    case "contains":
                        tokens.Add(new ConditionalAceToken { Type = ConditionalAceTokenType.Contains });
                        break;
                    case "any_of":
                        tokens.Add(new ConditionalAceToken { Type = ConditionalAceTokenType.AnyOf });
                        break;
                    case "member_of":
                        tokens.Add(new ConditionalAceToken { Type = ConditionalAceTokenType.MemberOf });
                        break;
                    case "device_member_of":
                        tokens.Add(new ConditionalAceToken { Type = ConditionalAceTokenType.DeviceMemberOf });
                        break;
                    case "true":
                        tokens.Add(new ConditionalAceToken { Type = ConditionalAceTokenType.LiteralInt, IntValue = 1 });
                        break;
                    case "false":
                        tokens.Add(new ConditionalAceToken { Type = ConditionalAceTokenType.LiteralInt, IntValue = 0 });
                        break;
                    default:
                        // Treat as a local attribute reference
                        tokens.Add(new ConditionalAceToken
                        {
                            Type = ConditionalAceTokenType.LocalAttribute,
                            StringValue = keyword
                        });
                        break;
                }
                continue;
            }

            // Skip unrecognized characters
            i++;
        }

        return tokens;
    }

    private static string ReadIdentifier(string expression, ref int i)
    {
        int start = i;
        while (i < expression.Length && (char.IsLetterOrDigit(expression[i]) || expression[i] == '.' || expression[i] == '_' || expression[i] == '-'))
            i++;
        return expression[start..i];
    }
}
