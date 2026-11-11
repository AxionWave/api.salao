namespace Lyra.Infrastructure.Persistence.Entities;

/// <summary>
/// Marca de que um aviso programado (lembrete, resumo do dia) já foi para a fila. A chave identifica o aviso
/// (<c>lembrete:{agendamento}:{horas}:{início}</c>, <c>resumo:{profissional}:{dia}</c>): inserir é o que garante
/// que ele sai uma vez só, mesmo com várias instâncias da API.
/// </summary>
public sealed class AvisoProgramado
{
    public string Chave { get; set; } = string.Empty;
    public int EmpresaId { get; set; }
    public DateTime DataCriacao { get; set; }
}
