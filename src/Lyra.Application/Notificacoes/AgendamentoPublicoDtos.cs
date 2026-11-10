namespace Lyra.Application.Notificacoes;

/// <summary>O que o cliente vê pelo link do e-mail, sem login. Só o primeiro nome dele.</summary>
public sealed record AgendamentoPublicoDto(
    string Empresa,
    string? Endereco,
    string? Telefone,
    string Cliente,
    string Profissional,
    IReadOnlyList<string> Servicos,
    DateTime Inicio,
    DateTime Fim,
    string Fuso,
    string Status,
    bool PodeConfirmar);
