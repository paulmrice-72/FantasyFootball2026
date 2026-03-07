namespace FF.SharedKernel;

public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; protected set; }

    protected Entity() { }

    public void SetUpdated()
    {
        UpdatedAt = DateTime.UtcNow;
    }
}