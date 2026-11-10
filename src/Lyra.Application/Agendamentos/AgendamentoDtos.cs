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
    DateTime? DataAtualizacao,
    int IntervaloAposMinutos,
    int? RemarcadoDeId,
    string? MotivoCancelamento);

public sealed record SalvarAgendamentoRequest(
    int ClienteId,
    IReadOnlyList<int> ServicoIds,
    int ProfissionalId,
    DateTime Inicio,
    string? Observacoes);

/// <param name="Motivo">Opcional, guardado ao cancelar.</param>
public sealed record AlterarStatusRequest(string Status, string? Motivo = null);

/// <param name="Ativo">Inativos só vêm quando ainda têm agendamentos, para continuarem visíveis na grade.</param>
public sealed record ProfissionalAgendaDto(
    int Id,
    string Nome,
    string Cor,
    IReadOnlyList<IntervaloAgendaDto> Disponibilidades,
    bool Ativo = true);

public sealed record IntervaloAgendaDto(int DiaSemana, string Inicio, string Fim);

/// <param name="ProfissionalIds">Quem atende. Vazio = todos.</param>
public sealed record ServicoAgendaDto(
    int Id,
    string Nome,
    int DuracaoMinutos,
    decimal Preco,
    int IntervaloAposMinutos,
    IReadOnlyList<int> ProfissionalIds);

public sealed record OpcoesAgendaResponse(
    IReadOnlyList<ProfissionalAgendaDto> Profissionais,
    IReadOnlyList<ServicoAgendaDto> Servicos,
    string FusoHorario);

/// <param name="Data">Dia no calendário da empresa.</param>
/// <param name="IgnorarAgendamentoId">Ao remarcar, o próprio agendamento não conta como ocupado.</param>
public sealed record HorariosLivresRequest(
    DateOnly Data,
    IReadOnlyList<int> ServicoIds,
    int? ProfissionalId,
    int? IgnorarAgendamentoId,
    int PassoMinutos = 15);

public sealed record HorarioLivreDto(DateTime Inicio, string Hora);

public sealed record HorariosLivresProfissionalDto(
    int ProfissionalId,
    string Nome,
    IReadOnlyList<HorarioLivreDto> Horarios);

public sealed record HorariosLivresResponse(
    DateOnly Data,
    int DuracaoMinutos,
    string FusoHorario,
    IReadOnlyList<HorariosLivresProfissionalDto> Profissionais);

public sealed record ListaAgendamentosResponse(
    IReadOnlyList<AgendamentoDto> Items);
