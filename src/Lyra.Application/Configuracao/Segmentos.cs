namespace Lyra.Application.Configuracao;

/// <summary>Modelos prontos por ramo de negócio. Empresas sem configuração usam <see cref="Padrao"/>.</summary>
public static class Segmentos
{
    public const string Padrao = "beleza";
    public const string FusoPadrao = "America/Sao_Paulo";

    private static TermoDto M(string singular, string plural) => new(singular, plural, "m");
    private static TermoDto F(string singular, string plural) => new(singular, plural, "f");

    public static readonly IReadOnlyList<SegmentoDto> Todos =
    [
        new("beleza", "Beleza e barbearia", new(
            M("Profissional", "Profissionais"),
            M("Serviço", "Serviços"),
            M("Cliente", "Clientes"),
            M("Horário", "Horários"))),
        new("saude", "Saúde e clínicas", new(
            M("Profissional", "Profissionais"),
            M("Procedimento", "Procedimentos"),
            M("Paciente", "Pacientes"),
            F("Consulta", "Consultas"))),
        new("pet", "Pet shop e veterinária", new(
            M("Profissional", "Profissionais"),
            M("Serviço", "Serviços"),
            M("Tutor", "Tutores"),
            M("Agendamento", "Agendamentos"))),
        new("esporte", "Quadras e espaços", new(
            M("Espaço", "Espaços"),
            F("Modalidade", "Modalidades"),
            M("Cliente", "Clientes"),
            F("Reserva", "Reservas"))),
        new("aulas", "Aulas e turmas", new(
            M("Instrutor", "Instrutores"),
            F("Aula", "Aulas"),
            M("Aluno", "Alunos"),
            F("Reserva", "Reservas"))),
        new("tecnico", "Oficinas e serviços técnicos", new(
            M("Técnico", "Técnicos"),
            M("Serviço", "Serviços"),
            M("Cliente", "Clientes"),
            M("Atendimento", "Atendimentos"))),
        new("outro", "Outro", new(
            M("Profissional", "Profissionais"),
            M("Serviço", "Serviços"),
            M("Cliente", "Clientes"),
            M("Agendamento", "Agendamentos"))),
    ];

    public static readonly IReadOnlyList<FusoHorarioDto> FusosHorarios =
    [
        new("America/Sao_Paulo", "Brasília (BRT)"),
        new("America/Noronha", "Fernando de Noronha"),
        new("America/Bahia", "Bahia"),
        new("America/Fortaleza", "Fortaleza / Nordeste"),
        new("America/Recife", "Recife"),
        new("America/Belem", "Belém"),
        new("America/Manaus", "Manaus / Amazonas"),
        new("America/Cuiaba", "Cuiabá / Mato Grosso"),
        new("America/Campo_Grande", "Campo Grande / MS"),
        new("America/Porto_Velho", "Porto Velho / Rondônia"),
        new("America/Boa_Vista", "Boa Vista / Roraima"),
        new("America/Rio_Branco", "Rio Branco / Acre"),
        new("Europe/Lisbon", "Lisboa"),
    ];

    public static SegmentoDto? Obter(string? id)
    {
        var chave = (id ?? string.Empty).Trim().ToLowerInvariant();
        return Todos.FirstOrDefault(s => s.Id == chave);
    }

    public static bool FusoValido(string? id) =>
        !string.IsNullOrWhiteSpace(id) && FusosHorarios.Any(f => f.Id == id.Trim());
}
