// FF.Domain/Enums/PlayerRole.cs
namespace FF.Domain.Enums;

public enum PlayerRole
{
    Unknown = 0,

    // Wide Receiver roles
    WR1Alpha = 10,       // target share > 25%, WOPR > 0.50
    SlotPossession = 11, // snap% > 60%, aDOT < 8
    DeepThreat = 12,     // aDOT > 14, target share < 15%

    // Running Back roles
    BellCow = 20,        // carry share > 60%, snap% > 65%
    PassCatcher = 21,    // targets/gm > 4, carry share < 30%
    Handcuff = 22,       // snap% < 30%, carries < 5/gm

    // Tight End roles
    SeamReceiver = 30,   // WOPR > 0.35, aDOT > 9
    BlockerSpot = 31,    // snap% < 50%, targets < 3/gm

    // Quarterback
    StartingQB = 40,     // completions > 0 in majority of games
    BackupQB = 41        // minimal passing volume
}