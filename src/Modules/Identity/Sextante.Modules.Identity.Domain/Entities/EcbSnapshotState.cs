namespace Sextante.Modules.Identity.Domain.Entities;

/// <summary>
/// Estado single-row do snapshot diário ECB. Surface para a UI
/// admin (warning chip quando <see cref="LastError"/> != null).
/// </summary>
public sealed class EcbSnapshotState
{
    public const int SingletonId = 1;
    public const int LastErrorMaxLength = 2000;

    public int Id { get; set; } = SingletonId;
    public DateTimeOffset? LastRunAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public string? LastError { get; set; }
}
