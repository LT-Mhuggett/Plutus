namespace Plutus.SharedKernel;

/// <summary>Authorization policy / scope names for the platform's scope-based endpoints (T1.2).
/// Shared here so both the Identity module (which defines the policies) and feature modules
/// (which apply [Authorize(Policy = …)]) reference one source without a module→module dependency.</summary>
public static class PlutusPolicies
{
    public const string PlatformAdmin = "platform-admin";
    public const string PortalTillsEnrol = "portal.tills.enrol";
    public const string Device = "device";
}
