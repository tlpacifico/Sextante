namespace Sextante.SharedKernel;

public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
    DateTimeOffset? DeletedAt { get; set; }
    int Version { get; set; }
}
