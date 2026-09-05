-- Lyra (app.salao / api.salao)
-- Sistema core.sistemas.codigo = LYR
-- Execute no Postgres (database base, schema core) depois do Core estar no ar
-- (Flyway V007: coluna sistema_id em core.perfis_acesso).
-- Admin bootstrap (email=1) recebe os módulos; ajuste o email se necessário.
-- Depois do seed: faça login de novo para renovar o JWT (claim modulos).

BEGIN;

INSERT INTO core.sistemas (nome, codigo, descricao, ativo, data_criacao)
SELECT 'Lyra', 'LYR', 'Sistema de salao (Lyra)', true, NOW()
WHERE NOT EXISTS (SELECT 1 FROM core.sistemas WHERE codigo = 'LYR');

WITH s AS (SELECT id FROM core.sistemas WHERE codigo = 'LYR' LIMIT 1)
INSERT INTO core.modulos (nome, descricao, codigo, url, ordem, ativo, data_criacao, sistema_id)
SELECT v.nome, v.descricao, v.codigo, v.url, v.ordem, true, NOW(), s.id
FROM s
CROSS JOIN (VALUES
  ('Lyra', 'Modulo de segurança / entrada no Lyra', 'LYRA000000', '/inicio', 0),
  ('Inicio Lyra', 'Modulo raiz legado (LYR0000000)', 'LYR0000000', '/inicio', 0),
  ('Agenda', 'Agenda e horarios', 'LYR0000001', '/agenda', 1),
  ('Servicos', 'Catalogo de servicos', 'LYR0000002', '/servicos', 2),
  ('Clientes', 'Clientes do salao', 'LYR0000003', '/clientes', 3),
  ('Profissionais', 'Equipe que atende', 'LYR0000004', '/profissionais', 4),
  ('Configuracoes', 'Unidade e perfis do Lyra', 'LYR0000005', '/configuracoes', 5)
) AS v(nome, descricao, codigo, url, ordem)
WHERE NOT EXISTS (SELECT 1 FROM core.modulos m WHERE m.codigo = v.codigo);

INSERT INTO core.usuario_modulo (ativo, data_criacao, usuario_id, modulo_id)
SELECT true, NOW(), u.id, m.id
FROM core.usuarios u
CROSS JOIN core.modulos m
JOIN core.sistemas s ON s.id = m.sistema_id AND s.codigo = 'LYR'
WHERE lower(u.email) = lower('1')
  AND NOT EXISTS (
    SELECT 1 FROM core.usuario_modulo um
    WHERE um.usuario_id = u.id AND um.modulo_id = m.id
  );

INSERT INTO core.perfil_modulo (ativo, data_criacao, perfil_id, modulo_id)
SELECT true, NOW(), p.id, m.id
FROM core.usuarios u
JOIN core.perfis_acesso p ON p.empresa_id = u.empresa_id
  AND p.nome IN ('Administrador', 'SuperAdmin', 'Cliente Admin', 'ClienteAdmin')
CROSS JOIN core.modulos m
JOIN core.sistemas s ON s.id = m.sistema_id AND s.codigo = 'LYR'
WHERE lower(u.email) = lower('1')
  AND NOT EXISTS (
    SELECT 1 FROM core.perfil_modulo pm
    WHERE pm.perfil_id = p.id AND pm.modulo_id = m.id
  );

INSERT INTO core.perfil_modulo_permissoes (
  perfil_modulo_id,
  pode_visualizar, pode_criar, pode_editar, pode_excluir,
  pode_aprovar, pode_exportar, pode_imprimir,
  pode_gerenciar_usuarios, pode_importar, pode_ativar_desativar,
  pode_ver_dados_sensiveis, pode_auditar, pode_configurar,
  pode_duplicar, pode_restaurar, pode_ver_relatorios, pode_gerar_relatorios,
  data_criacao
)
SELECT pm.id,
  true, true, true, true,
  true, true, true,
  true, true, true,
  true, true, true,
  true, true, true, true,
  NOW()
FROM core.usuarios u
JOIN core.perfis_acesso p ON p.empresa_id = u.empresa_id
  AND p.nome IN ('Administrador', 'SuperAdmin', 'Cliente Admin', 'ClienteAdmin')
JOIN core.perfil_modulo pm ON pm.perfil_id = p.id
JOIN core.modulos m ON m.id = pm.modulo_id
JOIN core.sistemas s ON s.id = m.sistema_id AND s.codigo = 'LYR'
WHERE lower(u.email) = lower('1')
  AND NOT EXISTS (
    SELECT 1 FROM core.perfil_modulo_permissoes pmp WHERE pmp.perfil_modulo_id = pm.id
  );

UPDATE core.perfil_modulo_permissoes pmp
SET
  pode_visualizar = true,
  pode_criar = true,
  pode_editar = true,
  pode_excluir = true,
  pode_aprovar = true,
  pode_exportar = true,
  pode_imprimir = true,
  pode_gerenciar_usuarios = true,
  pode_importar = true,
  pode_ativar_desativar = true,
  pode_ver_dados_sensiveis = true,
  pode_auditar = true,
  pode_configurar = true,
  pode_duplicar = true,
  pode_restaurar = true,
  pode_ver_relatorios = true,
  pode_gerar_relatorios = true,
  data_atualizacao = NOW()
FROM core.perfil_modulo pm
JOIN core.perfis_acesso p ON p.id = pm.perfil_id
JOIN core.modulos m ON m.id = pm.modulo_id
JOIN core.sistemas s ON s.id = m.sistema_id AND s.codigo = 'LYR'
WHERE pmp.perfil_modulo_id = pm.id
  AND p.nome IN ('Administrador', 'SuperAdmin', 'Cliente Admin', 'ClienteAdmin');

INSERT INTO core.perfis_acesso (nome, descricao, ativo, data_criacao, empresa_id, origem, sistema_id)
SELECT DISTINCT
  'Funcionário Lyra',
  'Operação do salão: agenda e clientes. Sem configurações, relatório financeiro nem cadastro de serviços.',
  true,
  NOW(),
  e.id,
  'SISTEMA',
  s.id
FROM core.empresas e
JOIN core.sistemas s ON s.codigo = 'LYR'
WHERE (
    EXISTS (
      SELECT 1
      FROM core.usuario_modulo um
      JOIN core.usuarios u ON u.id = um.usuario_id AND u.empresa_id = e.id
      JOIN core.modulos m ON m.id = um.modulo_id AND m.sistema_id = s.id
    )
    OR EXISTS (
      SELECT 1
      FROM core.perfil_modulo pm
      JOIN core.perfis_acesso p ON p.id = pm.perfil_id AND p.empresa_id = e.id
      JOIN core.modulos m ON m.id = pm.modulo_id AND m.sistema_id = s.id
    )
  )
  AND NOT EXISTS (
    SELECT 1 FROM core.perfis_acesso x
    WHERE x.empresa_id = e.id AND x.nome = 'Funcionário Lyra'
  );

UPDATE core.perfis_acesso p
SET origem = 'SISTEMA',
    sistema_id = s.id,
    descricao = 'Operação do salão: agenda e clientes. Sem configurações, relatório financeiro nem cadastro de serviços.',
    ativo = true,
    data_atualizacao = NOW()
FROM core.sistemas s
WHERE s.codigo = 'LYR'
  AND p.nome = 'Funcionário Lyra';

INSERT INTO core.perfil_modulo (ativo, data_criacao, perfil_id, modulo_id)
SELECT true, NOW(), p.id, m.id
FROM core.perfis_acesso p
JOIN core.modulos m ON m.codigo IN ('LYRA000000', 'LYR0000000', 'LYR0000001', 'LYR0000003')
WHERE p.nome = 'Funcionário Lyra'
  AND NOT EXISTS (
    SELECT 1 FROM core.perfil_modulo pm
    WHERE pm.perfil_id = p.id AND pm.modulo_id = m.id
  );

INSERT INTO core.perfil_modulo_permissoes (
  perfil_modulo_id,
  pode_visualizar, pode_criar, pode_editar, pode_excluir,
  pode_aprovar, pode_exportar, pode_imprimir,
  pode_gerenciar_usuarios, pode_importar, pode_ativar_desativar,
  pode_ver_dados_sensiveis, pode_auditar, pode_configurar,
  pode_duplicar, pode_restaurar, pode_ver_relatorios, pode_gerar_relatorios,
  data_criacao
)
SELECT pm.id,
  true,
  CASE WHEN m.codigo IN ('LYR0000001', 'LYR0000003') THEN true ELSE false END,
  CASE WHEN m.codigo IN ('LYR0000001', 'LYR0000003') THEN true ELSE false END,
  false,
  false, false, false,
  false, false, false,
  false, false, false,
  false, false, false, false,
  NOW()
FROM core.perfis_acesso p
JOIN core.perfil_modulo pm ON pm.perfil_id = p.id
JOIN core.modulos m ON m.id = pm.modulo_id
WHERE p.nome = 'Funcionário Lyra'
  AND m.codigo IN ('LYRA000000', 'LYR0000000', 'LYR0000001', 'LYR0000003')
  AND NOT EXISTS (
    SELECT 1 FROM core.perfil_modulo_permissoes pmp WHERE pmp.perfil_modulo_id = pm.id
  );

SELECT s.codigo AS sistema, m.codigo AS modulo, m.nome
FROM core.modulos m
JOIN core.sistemas s ON s.id = m.sistema_id
WHERE s.codigo = 'LYR'
ORDER BY m.ordem;

COMMIT;
