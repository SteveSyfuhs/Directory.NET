using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Schema;
using Directory.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Directory.Tests;

public class AccessControlTests
{
    private readonly AccessControlService _aclService = new(new SchemaService(), NullLogger<AccessControlService>.Instance);

    private static DirectoryObject CreateObjectWithSd(SecurityDescriptor sd)
    {
        var obj = new DirectoryObject
        {
            DistinguishedName = "CN=TestObject,OU=Test,DC=corp,DC=com",
            ObjectClass = ["top", "organizationalUnit"],
            ObjectCategory = "organizationalUnit",
            Cn = "TestObject",
        };

        // Serialize the SD and set it on the object so GetSecurityDescriptor can find it
        var sdBytes = sd.Serialize();
        obj.NTSecurityDescriptor = sdBytes;

        return obj;
    }

    // ── No DACL = full access ──────────────────────────────────────────

    [Fact]
    public void CheckAccess_NoDacl_GrantsAccess()
    {
        var sd = new SecurityDescriptor
        {
            OwnerSid = "S-1-5-21-1-2-3-500",
            Control = SdControlFlags.SelfRelative, // no DaclPresent
        };

        var obj = CreateObjectWithSd(sd);
        var result = _aclService.CheckAccess("S-1-5-21-1-2-3-1001", obj, AccessMask.ReadProperty);

        Assert.True(result);
    }

    // ── Allow ACE grants access ────────────────────────────────────────

    [Fact]
    public void CheckAccess_AllowAce_GrantsAccess()
    {
        var callerSid = "S-1-5-21-1-2-3-1001";

        var sd = new SecurityDescriptor
        {
            OwnerSid = "S-1-5-21-1-2-3-500",
            Control = SdControlFlags.DaclPresent | SdControlFlags.SelfRelative,
            Dacl = new AccessControlList
            {
                Aces =
                [
                    new AccessControlEntry
                    {
                        Type = AceType.AccessAllowed,
                        Flags = AceFlags.None,
                        Mask = AccessMask.ReadProperty,
                        TrusteeSid = callerSid
                    }
                ]
            }
        };

        var obj = CreateObjectWithSd(sd);
        var result = _aclService.CheckAccess(callerSid, obj, AccessMask.ReadProperty);

        Assert.True(result);
    }

    // ── Deny ACE overrides Allow ACE ───────────────────────────────────

    [Fact]
    public void CheckAccess_DenyAceOverridesAllow_DeniesAccess()
    {
        var callerSid = "S-1-5-21-1-2-3-1001";

        var sd = new SecurityDescriptor
        {
            OwnerSid = "S-1-5-21-1-2-3-500",
            Control = SdControlFlags.DaclPresent | SdControlFlags.SelfRelative,
            Dacl = new AccessControlList
            {
                Aces =
                [
                    new AccessControlEntry
                    {
                        Type = AceType.AccessDenied,
                        Flags = AceFlags.None,
                        Mask = AccessMask.ReadProperty,
                        TrusteeSid = callerSid
                    },
                    new AccessControlEntry
                    {
                        Type = AceType.AccessAllowed,
                        Flags = AceFlags.None,
                        Mask = AccessMask.ReadProperty,
                        TrusteeSid = callerSid
                    }
                ]
            }
        };

        var obj = CreateObjectWithSd(sd);
        var result = _aclService.CheckAccess(callerSid, obj, AccessMask.ReadProperty);

        Assert.False(result);
    }

    // ── No matching ACE defaults to deny ───────────────────────────────

    [Fact]
    public void CheckAccess_NoMatchingAce_DeniesAccess()
    {
        var callerSid = "S-1-5-21-1-2-3-1001";
        var otherSid = "S-1-5-21-1-2-3-9999";

        var sd = new SecurityDescriptor
        {
            OwnerSid = "S-1-5-21-1-2-3-500",
            Control = SdControlFlags.DaclPresent | SdControlFlags.SelfRelative,
            Dacl = new AccessControlList
            {
                Aces =
                [
                    new AccessControlEntry
                    {
                        Type = AceType.AccessAllowed,
                        Flags = AceFlags.None,
                        Mask = AccessMask.ReadProperty,
                        TrusteeSid = otherSid // Different SID
                    }
                ]
            }
        };

        var obj = CreateObjectWithSd(sd);
        var result = _aclService.CheckAccess(callerSid, obj, AccessMask.ReadProperty);

        Assert.False(result);
    }

    // ── Owner has implicit ReadControl and WriteDacl ────────────────────

    [Fact]
    public void CheckAccess_Owner_HasImplicitReadControl()
    {
        var ownerSid = "S-1-5-21-1-2-3-500";

        var sd = new SecurityDescriptor
        {
            OwnerSid = ownerSid,
            Control = SdControlFlags.DaclPresent | SdControlFlags.SelfRelative,
            Dacl = new AccessControlList { Aces = [] } // Empty DACL
        };

        var obj = CreateObjectWithSd(sd);

        // Owner gets ReadControl implicitly
        Assert.True(_aclService.CheckAccess(ownerSid, obj, AccessMask.ReadControl));
    }

    [Fact]
    public void CheckAccess_Owner_HasImplicitWriteDacl()
    {
        var ownerSid = "S-1-5-21-1-2-3-500";

        var sd = new SecurityDescriptor
        {
            OwnerSid = ownerSid,
            Control = SdControlFlags.DaclPresent | SdControlFlags.SelfRelative,
            Dacl = new AccessControlList { Aces = [] }
        };

        var obj = CreateObjectWithSd(sd);

        Assert.True(_aclService.CheckAccess(ownerSid, obj, AccessMask.WriteDacl));
    }

    [Fact]
    public void CheckAccess_Owner_StillNeedsExplicitForOtherRights()
    {
        var ownerSid = "S-1-5-21-1-2-3-500";

        var sd = new SecurityDescriptor
        {
            OwnerSid = ownerSid,
            Control = SdControlFlags.DaclPresent | SdControlFlags.SelfRelative,
            Dacl = new AccessControlList { Aces = [] }
        };

        var obj = CreateObjectWithSd(sd);

        // Owner only gets ReadControl + WriteDacl implicitly, not WriteProperty
        Assert.False(_aclService.CheckAccess(ownerSid, obj, AccessMask.WriteProperty));
    }

    // ── GenericAll grants everything ───────────────────────────────────

    [Fact]
    public void CheckAccess_GenericAllAce_GrantsAnyAccess()
    {
        var callerSid = "S-1-5-21-1-2-3-1001";

        var sd = new SecurityDescriptor
        {
            OwnerSid = "S-1-5-21-1-2-3-500",
            Control = SdControlFlags.DaclPresent | SdControlFlags.SelfRelative,
            Dacl = new AccessControlList
            {
                Aces =
                [
                    new AccessControlEntry
                    {
                        Type = AceType.AccessAllowed,
                        Flags = AceFlags.None,
                        Mask = AccessMask.GenericAll,
                        TrusteeSid = callerSid
                    }
                ]
            }
        };

        var obj = CreateObjectWithSd(sd);

        Assert.True(_aclService.CheckAccess(callerSid, obj, AccessMask.WriteProperty));
        Assert.True(_aclService.CheckAccess(callerSid, obj, AccessMask.ReadProperty));
        Assert.True(_aclService.CheckAccess(callerSid, obj, AccessMask.DeleteObject));
    }

    // ── InheritOnly ACE should not apply to the object itself ──────────

    [Fact]
    public void CheckAccess_InheritOnlyAce_DoesNotApplyToObject()
    {
        var callerSid = "S-1-5-21-1-2-3-1001";

        var sd = new SecurityDescriptor
        {
            OwnerSid = "S-1-5-21-1-2-3-500",
            Control = SdControlFlags.DaclPresent | SdControlFlags.SelfRelative,
            Dacl = new AccessControlList
            {
                Aces =
                [
                    new AccessControlEntry
                    {
                        Type = AceType.AccessAllowed,
                        Flags = AceFlags.InheritOnly | AceFlags.ContainerInherit,
                        Mask = AccessMask.ReadProperty,
                        TrusteeSid = callerSid
                    }
                ]
            }
        };

        var obj = CreateObjectWithSd(sd);

        // InheritOnly ACE is only for children, not for this object
        Assert.False(_aclService.CheckAccess(callerSid, obj, AccessMask.ReadProperty));
    }

    // ── Everyone SID matches any caller ────────────────────────────────

    [Fact]
    public void CheckAccess_EveryoneSid_MatchesAnyCaller()
    {
        var callerSid = "S-1-5-21-1-2-3-12345";

        var sd = new SecurityDescriptor
        {
            OwnerSid = "S-1-5-21-1-2-3-500",
            Control = SdControlFlags.DaclPresent | SdControlFlags.SelfRelative,
            Dacl = new AccessControlList
            {
                Aces =
                [
                    new AccessControlEntry
                    {
                        Type = AceType.AccessAllowed,
                        Flags = AceFlags.None,
                        Mask = AccessMask.ReadProperty,
                        TrusteeSid = WellKnownSids.Everyone
                    }
                ]
            }
        };

        var obj = CreateObjectWithSd(sd);

        Assert.True(_aclService.CheckAccess(callerSid, obj, AccessMask.ReadProperty));
    }

    // ── AuthenticatedUsers matches non-anonymous caller ────────────────

    [Fact]
    public void CheckAccess_AuthenticatedUsers_MatchesNonAnonymous()
    {
        var callerSid = "S-1-5-21-1-2-3-1001";

        var sd = new SecurityDescriptor
        {
            OwnerSid = "S-1-5-21-1-2-3-500",
            Control = SdControlFlags.DaclPresent | SdControlFlags.SelfRelative,
            Dacl = new AccessControlList
            {
                Aces =
                [
                    new AccessControlEntry
                    {
                        Type = AceType.AccessAllowed,
                        Flags = AceFlags.None,
                        Mask = AccessMask.ReadProperty,
                        TrusteeSid = WellKnownSids.AuthenticatedUsers
                    }
                ]
            }
        };

        var obj = CreateObjectWithSd(sd);

        Assert.True(_aclService.CheckAccess(callerSid, obj, AccessMask.ReadProperty));
    }

    [Fact]
    public void CheckAccess_AuthenticatedUsers_DoesNotMatchAnonymous()
    {
        var anonymousSid = "S-1-5-7"; // Anonymous SID

        var sd = new SecurityDescriptor
        {
            OwnerSid = "S-1-5-21-1-2-3-500",
            Control = SdControlFlags.DaclPresent | SdControlFlags.SelfRelative,
            Dacl = new AccessControlList
            {
                Aces =
                [
                    new AccessControlEntry
                    {
                        Type = AceType.AccessAllowed,
                        Flags = AceFlags.None,
                        Mask = AccessMask.ReadProperty,
                        TrusteeSid = WellKnownSids.AuthenticatedUsers
                    }
                ]
            }
        };

        var obj = CreateObjectWithSd(sd);

        Assert.False(_aclService.CheckAccess(anonymousSid, obj, AccessMask.ReadProperty));
    }

    // ── GetEffectiveAccess tests ───────────────────────────────────────

    [Fact]
    public void GetEffectiveAccess_DenyMasksOutAllowed()
    {
        var callerSid = "S-1-5-21-1-2-3-1001";

        var sd = new SecurityDescriptor
        {
            OwnerSid = "S-1-5-21-1-2-3-500",
            Control = SdControlFlags.DaclPresent | SdControlFlags.SelfRelative,
            Dacl = new AccessControlList
            {
                Aces =
                [
                    new AccessControlEntry
                    {
                        Type = AceType.AccessAllowed,
                        Flags = AceFlags.None,
                        Mask = AccessMask.ReadProperty | AccessMask.WriteProperty,
                        TrusteeSid = callerSid
                    },
                    new AccessControlEntry
                    {
                        Type = AceType.AccessDenied,
                        Flags = AceFlags.None,
                        Mask = AccessMask.WriteProperty,
                        TrusteeSid = callerSid
                    }
                ]
            }
        };

        var obj = CreateObjectWithSd(sd);
        var effective = _aclService.GetEffectiveAccess(callerSid, obj);

        // ReadProperty should be granted, WriteProperty should be denied
        Assert.NotEqual(0, effective & AccessMask.ReadProperty);
        Assert.Equal(0, effective & AccessMask.WriteProperty);
    }

    // ── ACE inheritance ────────────────────────────────────────────────

    [Fact]
    public void InheritAces_ContainerInherit_PropagatesAce()
    {
        var parentSd = new SecurityDescriptor
        {
            OwnerSid = "S-1-5-21-1-2-3-500",
            Control = SdControlFlags.DaclPresent,
            Dacl = new AccessControlList
            {
                Aces =
                [
                    new AccessControlEntry
                    {
                        Type = AceType.AccessAllowed,
                        Flags = AceFlags.ContainerInherit,
                        Mask = AccessMask.ReadProperty,
                        TrusteeSid = "S-1-5-21-1-2-3-1001"
                    }
                ]
            }
        };

        var childSd = new SecurityDescriptor
        {
            OwnerSid = "S-1-5-21-1-2-3-500",
            Control = SdControlFlags.DaclPresent,
            Dacl = new AccessControlList { Aces = [] }
        };

        var result = _aclService.InheritAces(parentSd, childSd);

        Assert.NotEmpty(result.Dacl.Aces);
        var inheritedAce = result.Dacl.Aces[0];
        Assert.True((inheritedAce.Flags & AceFlags.Inherited) != 0);
        Assert.Equal(AccessMask.ReadProperty, inheritedAce.Mask);
    }

    [Fact]
    public void InheritAces_DaclProtected_SkipsInheritance()
    {
        var parentSd = new SecurityDescriptor
        {
            OwnerSid = "S-1-5-21-1-2-3-500",
            Control = SdControlFlags.DaclPresent,
            Dacl = new AccessControlList
            {
                Aces =
                [
                    new AccessControlEntry
                    {
                        Type = AceType.AccessAllowed,
                        Flags = AceFlags.ContainerInherit,
                        Mask = AccessMask.ReadProperty,
                        TrusteeSid = "S-1-5-21-1-2-3-1001"
                    }
                ]
            }
        };

        var childSd = new SecurityDescriptor
        {
            OwnerSid = "S-1-5-21-1-2-3-500",
            Control = SdControlFlags.DaclPresent | SdControlFlags.DaclProtected,
            Dacl = new AccessControlList { Aces = [] }
        };

        var result = _aclService.InheritAces(parentSd, childSd);

        // Protected DACL should not inherit parent ACEs
        Assert.Empty(result.Dacl.Aces);
    }
}
