namespace DestroyChecker.Core.Models
{
    public enum ItemSafety
    {
        Safe,   // Green — safe to destroy
        Check,  // Yellow — verify before destroying
        Keep    // Red — do NOT destroy
    }
}
