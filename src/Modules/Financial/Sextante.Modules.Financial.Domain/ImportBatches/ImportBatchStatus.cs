namespace Sextante.Modules.Financial.Domain.ImportBatches;

public enum ImportBatchStatus
{
    Parsing = 0,
    PreviewReady = 1,
    Confirming = 2,
    Importing = 3,
    Completed = 4,
    Failed = 5,
}
