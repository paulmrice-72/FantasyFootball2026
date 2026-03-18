// FF.Domain/Enums/GameScript.cs
namespace FF.Domain.Enums;

public enum GameScript
{
    Unknown = 0,
    BlowoutWin = 1,    // favoured by 10+ — RB volume up, WR/TE volume down
    Competitive = 2,   // spread within 7 — neutral multipliers
    Trailing = 3       // underdog by 10+ — RB volume down, WR/TE volume up
}