using Directory.Core.Models;
using Directory.Security;
using Xunit;

namespace Directory.Tests;

public class FineGrainedPasswordPolicyTests
{
    // ── PSO precedence resolution tests ────────────────────────────────
    // These tests validate PasswordSettingsObject model behavior and precedence
    // logic as defined in MS-ADTS 3.1.1.5.2.

    [Fact]
    public void PasswordSettingsObject_Defaults_AreReasonable()
    {
        var pso = new PasswordSettingsObject();

        Assert.Equal(int.MaxValue, pso.Precedence);
        Assert.Equal(24, pso.PasswordHistoryLength);
        Assert.True(pso.ComplexityEnabled);
        Assert.Equal(7, pso.MinimumPasswordLength);
        Assert.False(pso.ReversibleEncryption);
    }

    [Fact]
    public void PasswordSettingsObject_LockoutDefaults_AreThirtyMinutes()
    {
        var pso = new PasswordSettingsObject();

        Assert.Equal(-TimeSpan.FromMinutes(30).Ticks, pso.LockoutObservationWindow);
        Assert.Equal(-TimeSpan.FromMinutes(30).Ticks, pso.LockoutDuration);
        Assert.Equal(0, pso.LockoutThreshold); // 0 = no lockout by default
    }

    [Fact]
    public void PasswordSettingsObject_PasswordAgeDefaults_AreCorrect()
    {
        var pso = new PasswordSettingsObject();

        Assert.Equal(-TimeSpan.FromDays(1).Ticks, pso.MinimumPasswordAge);
        Assert.Equal(-TimeSpan.FromDays(42).Ticks, pso.MaximumPasswordAge);
    }

    [Fact]
    public void PsoPrecedence_LowerValueWins()
    {
        // Per MS-ADTS: the PSO with the lowest msDS-PasswordSettingsPrecedence wins
        var psoLow = new PasswordSettingsObject { Precedence = 10, Dn = "CN=PSO1,CN=PSC" };
        var psoHigh = new PasswordSettingsObject { Precedence = 20, Dn = "CN=PSO2,CN=PSC" };

        var candidates = new List<PasswordSettingsObject> { psoHigh, psoLow };

        var winner = candidates.OrderBy(p => p.Precedence).ThenBy(p => p.Dn, StringComparer.OrdinalIgnoreCase).First();

        Assert.Equal("CN=PSO1,CN=PSC", winner.Dn);
        Assert.Equal(10, winner.Precedence);
    }

    [Fact]
    public void PsoPrecedence_TieBreakByDn()
    {
        // When precedence is equal, the PSO with the lowest DN (alphabetically) wins
        var psoA = new PasswordSettingsObject { Precedence = 10, Dn = "CN=AAAA,CN=PSC" };
        var psoB = new PasswordSettingsObject { Precedence = 10, Dn = "CN=BBBB,CN=PSC" };

        var candidates = new List<PasswordSettingsObject> { psoB, psoA };

        var winner = candidates.OrderBy(p => p.Precedence).ThenBy(p => p.Dn, StringComparer.OrdinalIgnoreCase).First();

        Assert.Equal("CN=AAAA,CN=PSC", winner.Dn);
    }

    [Fact]
    public void DirectPsoLink_BeatsGroupLinkedPso()
    {
        // Per MS-ADTS: PSOs linked directly to the user take priority over group-linked PSOs,
        // even if the group-linked PSO has lower precedence.
        var userDn = "CN=TestUser,OU=Users,DC=corp,DC=com";
        var groupDn = "CN=SalesGroup,OU=Groups,DC=corp,DC=com";

        var directPso = new PasswordSettingsObject
        {
            Precedence = 20,
            Dn = "CN=DirectPSO,CN=PSC",
            AppliesTo = [userDn]
        };

        var groupPso = new PasswordSettingsObject
        {
            Precedence = 10, // lower precedence value = higher priority normally
            Dn = "CN=GroupPSO,CN=PSC",
            AppliesTo = [groupDn]
        };

        // Simulate the resolution logic from FineGrainedPasswordPolicyService
        var userGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { groupDn };
        var candidates = new List<(PasswordSettingsObject Pso, bool IsDirect)>();

        // Check direct links
        foreach (var pso in new[] { directPso, groupPso })
        {
            if (pso.AppliesTo.Any(dn => string.Equals(dn, userDn, StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add((pso, true));
            }
        }

        // Check group links
        foreach (var pso in new[] { directPso, groupPso })
        {
            if (candidates.Any(c => c.IsDirect && c.Pso.Dn == pso.Dn))
                continue;

            if (pso.AppliesTo.Any(dn => userGroups.Contains(dn)))
            {
                candidates.Add((pso, false));
            }
        }

        // Direct candidates take priority
        var directCandidates = candidates.Where(c => c.IsDirect).ToList();
        var effectiveCandidates = directCandidates.Count > 0 ? directCandidates : candidates;

        var winner = effectiveCandidates
            .OrderBy(c => c.Pso.Precedence)
            .ThenBy(c => c.Pso.Dn, StringComparer.OrdinalIgnoreCase)
            .First();

        Assert.Equal("CN=DirectPSO,CN=PSC", winner.Pso.Dn);
    }

    [Fact]
    public void MultiplePsos_LowestPrecedenceWins()
    {
        var userDn = "CN=TestUser,OU=Users,DC=corp,DC=com";

        var pso1 = new PasswordSettingsObject
        {
            Precedence = 30,
            Dn = "CN=PSO1,CN=PSC",
            AppliesTo = [userDn]
        };

        var pso2 = new PasswordSettingsObject
        {
            Precedence = 10,
            Dn = "CN=PSO2,CN=PSC",
            AppliesTo = [userDn]
        };

        var pso3 = new PasswordSettingsObject
        {
            Precedence = 20,
            Dn = "CN=PSO3,CN=PSC",
            AppliesTo = [userDn]
        };

        var all = new List<PasswordSettingsObject> { pso1, pso2, pso3 };

        var winner = all
            .OrderBy(p => p.Precedence)
            .ThenBy(p => p.Dn, StringComparer.OrdinalIgnoreCase)
            .First();

        Assert.Equal("CN=PSO2,CN=PSC", winner.Dn);
        Assert.Equal(10, winner.Precedence);
    }

    [Fact]
    public void PsoOverridesDomainDefault_MinLength()
    {
        var pso = new PasswordSettingsObject
        {
            MinimumPasswordLength = 14,
            ComplexityEnabled = true,
        };

        // Domain default is 7; PSO overrides it
        int effectiveMinLength = pso.MinimumPasswordLength;
        Assert.Equal(14, effectiveMinLength);
    }

    [Fact]
    public void NoPsoApplicable_FallsThroughToDefault()
    {
        var userDn = "CN=TestUser,OU=Users,DC=corp,DC=com";

        var pso = new PasswordSettingsObject
        {
            Precedence = 10,
            Dn = "CN=PSO1,CN=PSC",
            AppliesTo = ["CN=OtherUser,OU=Users,DC=corp,DC=com"] // not this user
        };

        var allPsos = new List<PasswordSettingsObject> { pso };

        var applicablePsos = allPsos
            .Where(p => p.AppliesTo.Any(dn => string.Equals(dn, userDn, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // No PSO applies to this user
        Assert.Empty(applicablePsos);
        // Caller should use domain default policy
    }

    [Fact]
    public void PsoLockoutSettings_CustomValues()
    {
        var pso = new PasswordSettingsObject
        {
            LockoutThreshold = 5,
            LockoutDuration = -TimeSpan.FromMinutes(15).Ticks,
            LockoutObservationWindow = -TimeSpan.FromMinutes(15).Ticks,
        };

        Assert.Equal(5, pso.LockoutThreshold);
        Assert.Equal(TimeSpan.FromMinutes(15), TimeSpan.FromTicks(Math.Abs(pso.LockoutDuration)));
        Assert.Equal(TimeSpan.FromMinutes(15), TimeSpan.FromTicks(Math.Abs(pso.LockoutObservationWindow)));
    }

    [Fact]
    public void LockoutPolicy_FromPso_ComputesCorrectly()
    {
        var pso = new PasswordSettingsObject
        {
            LockoutThreshold = 3,
            LockoutDuration = -TimeSpan.FromMinutes(10).Ticks,
            LockoutObservationWindow = -TimeSpan.FromMinutes(5).Ticks,
            MaximumPasswordAge = -TimeSpan.FromDays(90).Ticks,
            MinimumPasswordAge = -TimeSpan.FromDays(2).Ticks,
        };

        // Simulate what GetLockoutPolicyAsync does
        var policy = new LockoutPolicy
        {
            LockoutThreshold = pso.LockoutThreshold,
            LockoutDuration = TimeSpan.FromTicks(Math.Abs(pso.LockoutDuration)),
            ObservationWindow = TimeSpan.FromTicks(Math.Abs(pso.LockoutObservationWindow)),
            MaxPasswordAge = TimeSpan.FromTicks(Math.Abs(pso.MaximumPasswordAge)),
            MinPasswordAge = TimeSpan.FromTicks(Math.Abs(pso.MinimumPasswordAge)),
        };

        Assert.Equal(3, policy.LockoutThreshold);
        Assert.Equal(TimeSpan.FromMinutes(10), policy.LockoutDuration);
        Assert.Equal(TimeSpan.FromMinutes(5), policy.ObservationWindow);
        Assert.Equal(TimeSpan.FromDays(90), policy.MaxPasswordAge);
        Assert.Equal(TimeSpan.FromDays(2), policy.MinPasswordAge);
    }

    [Fact]
    public void PsoAppliesTo_MultipleEntities()
    {
        var pso = new PasswordSettingsObject
        {
            AppliesTo =
            [
                "CN=User1,OU=Users,DC=corp,DC=com",
                "CN=Admins,OU=Groups,DC=corp,DC=com",
                "CN=User2,OU=Users,DC=corp,DC=com"
            ]
        };

        Assert.Equal(3, pso.AppliesTo.Count);
        Assert.Contains("CN=User1,OU=Users,DC=corp,DC=com", pso.AppliesTo);
        Assert.Contains("CN=Admins,OU=Groups,DC=corp,DC=com", pso.AppliesTo);
    }
}
