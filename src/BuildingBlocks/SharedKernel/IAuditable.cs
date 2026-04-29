namespace Sextante.SharedKernel;

/// <summary>
/// Audit base sem soft-delete. Reference data e singletons que não
/// são "soft-arquivados" (ex.: <c>Currency</c>, <c>EcbSnapshotState</c>)
/// implementam apenas este contrato.
/// </summary>
public interface IVersioned
{
    DateTimeOffset CreatedAt { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
    int Version { get; set; }
}

/// <summary>
/// Audit + soft-delete. Entidades de negócio implementam este contrato
/// para herdar população automática de <c>CreatedAt</c>/<c>UpdatedAt</c>
/// e a query filter <c>DeletedAt is null</c>.
/// </summary>
public interface IAuditable : IVersioned
{
    DateTimeOffset? DeletedAt { get; set; }
}
