namespace Lyra.Application.Agendamentos;

public sealed record AgendamentoServicoDto(
    int Id,
    string Nome,
    int DuracaoMinutos,
    decimal Preco);

public sealed record AgendamentoDto(
    int Id,
    int EmpresaId,
    int ClienteId,
    string ClienteNome,
    string ClienteTelefone,
    IReadOnlyList<AgendamentoServicoDto> Servicos,
    string ServicoNome,
    int DuracaoMinutos,
    decimal Preco,
    int ProfissionalId,
    string ProfissionalNome,
    string ProfissionalCor,
    DateTime Inicio,
    DateTime Fim,
    string Status,
    string? Observacoes,
    DateTime DataCriacao,
    DateTime? DataAtualizacao);

public sealed record SalvarAgendamentoRequest(
    int ClienteId,
    IReadOnlyList<int> ServicoIds,
    int ProfissionalId,
    DateTime Inicio,
    string? Observacoes);

public sealed record AlterarStatusRequest(string Status);

public sealed record ProfissionalAgendaDto(
    int Id,
    string Nome,
    string Cor,
    IReadOnlyList<IntervaloAgendaDto> Disponibilidades);

public sealed record IntervaloAgendaDto(int DiaSemana, string Inicio, string Fim);

public sealed record ServicoAgendaDto(
    int Id,
    string Nome,
    int DuracaoMinutos,
    decimal Preco);

public sealed record OpcoesAgendaResponse(
    IReadOnlyList<ProfissionalAgendaDto> Profissionais,
    IReadOnlyList<ServicoAgendaDto> Servicos);

public sealed record ListaAgendamentosResponse(
    IReadOnlyList<AgendamentoDto> Items);
