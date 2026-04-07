namespace FF.WebBlazor.Services;

public class LeagueStateService
{
    private string? _activeLeagueId;

    public string? ActiveLeagueId
    {
        get => _activeLeagueId;
        set
        {
            if (_activeLeagueId == value) return;
            _activeLeagueId = value;
            OnLeagueChanged?.Invoke();
        }
    }

    public event Action? OnLeagueChanged;
}