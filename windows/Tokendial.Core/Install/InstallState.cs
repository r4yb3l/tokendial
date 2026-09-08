namespace Tokendial.Core.Install;

/// <summary>How far a provider's tool has come: the three steps the settings row draws, plus the connected end state.</summary>
public enum InstallState
{
    NotInstalled,
    Installed,
    SignedIn,
    Connected
}

public static class InstallProgress
{
    public static InstallState Of(bool installed, bool signedIn, bool connected)
    {
        if (signedIn) return connected ? InstallState.Connected : InstallState.SignedIn;
        return installed ? InstallState.Installed : InstallState.NotInstalled;
    }
}
