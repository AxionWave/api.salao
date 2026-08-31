-- Lyra (app.salao / api.salao)
-- Sistema core.sistemas.codigo = LYR
-- Execute no Postgres (database base, schema core) depois do Core estar no ar.
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
  ('Inicio Lyra', 'Modulo raiz do Lyra (salao)', 'LYR0000000', '/inicio', 0),
  ('Agenda', 'Agenda e horarios', 'LYR0000001', '/agenda', 1),
  ('Servicos', 'Catalogo de servicos', 'LYR0000002', '/servicos', 2),
  ('Clientes', 'Clientes do salao', 'LYR0000003', '/clientes', 3)
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
JOIN core.perfis_acesso p ON p.empresa_id = u.empresa_id AND p.nome IN ('Administrador', 'SuperAdmin')
CROSS JOIN core.modulos m
JOIN core.sistemas s ON s.id = m.sistema_id AND s.codigo = 'LYR'
WHERE lower(u.email) = lower('1')
  AND NOT EXISTS (
    SELECT 1 FROM core.perfil_modulo pm
    WHERE pm.perfil_id = p.id AND pm.modulo_id = m.id
  );

SELECT s.codigo AS sistema, m.codigo AS modulo, m.nome
FROM core.modulos m
JOIN core.sistemas s ON s.id = m.sistema_id
WHERE s.codigo = 'LYR'
ORDER BY m.ordem;

COMMIT;
