namespace EquipmentManagement.Infrastructure.DataImports;

/// <summary>
/// The pt-PT wording for an import session's own outcomes — shared by the
/// Consultant and Equipment imports, which handle a session identically.
/// These reach the person importing (as the response's title), so they are
/// never English and never name an internal status (ADR-070).
/// </summary>
internal static class ImportSessionMessages
{
    public const string NotFound = "Não foi possível encontrar esta importação.";

    public const string NotCancellable = "Só é possível cancelar uma importação que ainda esteja pendente.";

    public const string HasErrors = "Esta importação ainda tem linhas com erros e não pode ser confirmada.";

    public const string Expired = "Esta importação expirou. Carregue o ficheiro novamente.";

    public const string NotConfirmable = "Esta importação já foi concluída ou cancelada e não pode ser confirmada.";

    public const string Stale = "Os dados da aplicação mudaram desde a pré-visualização. Nada foi importado.";

    public const string Conflict = "Outra alteração entrou em conflito com esta importação. Nada foi importado.";
}
