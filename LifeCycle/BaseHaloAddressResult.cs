namespace CrowdControl.Games.Packs.MCCHaloCE.LifeCycle;

// Specifies the result of attempting to recalculate the base halo1.dll memory address.
public enum BaseHaloAddressResult
{
    Failure, // Could not be calculated.
    RecalculatedDifferentFromPrevious, // Was calculated, but it changed, most likely due to a process change.
    WasAlreadyCorrect, // Was calculated, and was the same as the previous one.
}