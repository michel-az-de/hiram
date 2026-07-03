# Plano de estabilização e go-live

> Levantamento de 2026-07-03. Fonte: auditoria completa de planos, código, deploy e CI.
> Issues correspondentes cadastradas no GitHub, numeradas de #1 a #20.

## 1. Diagnóstico

### O que está pronto

- F0 a F3.1 concluídas: outbox transacional, email (SMTP + Resend), idempotência, DLQ com replay, templates Scriban, web push VAPID, webhooks HMAC, ledger de créditos.
- Código limpo: zero TODO, zero catch engolido, zero async void, IClock em tudo, secrets protegidos com Data Protection. 129 testes unitários e 43 arquivos de integração com Testcontainers, tudo verde.
- Onda 0 e Onda 1 do plano de absorção do EasyStok estão muito mais avançadas do que o plano registra: ingestão de eventos, motor de rotinas, consentimento, bloqueios, adiamento por janela, claims de mensagem, manifests k3s, KEDA, health checks e graceful drain já existem no código. O plano precisa ser reconciliado (#11).
- Documentação pedagógica e console de demo (ADR-021) implementados. Site de marketing publicado em https://michel-az-de.github.io/hiram/ (Pages habilitado e deploy corrigido em 2026-07-03).

### O que este levantamento encontrou e já foi corrigido

- 21 commits locais sem push, cinco dias sem validação de CI. Enviados, e o CI acusou o problema abaixo.
- Build da imagem Docker quebrado no CI por vulnerabilidade alta no Microsoft.OpenApi 2.0.0 (NU1903 vira erro no publish Release). Corrigido com pin na versão 2.9.0.
- GitHub Pages desabilitado derrubava o workflow do site em todo push. Habilitado, deploy verde.

### O que falta, em uma frase

O produto está pronto para demonstrar e o código para operar; falta amarrar a operação real (TLS, secrets, backup, observabilidade persistente) e fechar o lado EasyStok (emissão durável, paridade, callbacks, cutover).

## 2. Marcos até o go-live

### Marco 1: demo vendável no ar (1 semana)

Objetivo comercial: uma URL pública onde um prospect vê o produto funcionando de verdade.

| Issue | Entrega |
|---|---|
| #2 | Ingress e TLS reais no k3s da VM de demo |
| #3 | Secret k8s com valores reais, sem CHANGE_ME |
| #15 | Stack na VM Azure, console live, tenant shadow com Mailpit |
| #16 | Roteiro de demo de 10 minutos ensaiado |

Critério do marco: demo completa (submit, trace, DLQ, replay, ledger) executável ao vivo, sem improviso, com o custo da VM finalmente justificado.

### Marco 2: fundação de produção estável (1 a 2 semanas, em paralelo com o marco 1)

| Issue | Entrega |
|---|---|
| #4 | Imagens no GHCR com tag por SHA |
| #8 | Deploy rastreável em um comando com smoke test |
| #5 | Backup diário automático e restore comprovado |
| #6 | Observabilidade persistente (PVC) e dashboards de SLO |
| #7 | Runbook de incidente P1 com alertas |
| #11 | Plano de absorção reconciliado com o código |

Critério do marco: qualquer commit no main vira imagem rastreável, o ambiente sobrevive a restart sem perder histórico, e um incidente às 3h da manhã tem runbook.

### Marco 3: shadow do EasyStok (2 a 3 semanas)

| Issue | Entrega |
|---|---|
| #12 | Emissão durável no EasyStok, provisioning shadow, coleta de paridade |
| #13 | Callbacks de provider (delivered, bounce, complaint) |

Gate: 7 dias de shadow contínuo, zero erro de ingestão, 3 séries de paridade coletadas.

### Marco 4: cutover e go-live (1 semana após o gate)

| Issue | Entrega |
|---|---|
| #14 | Dashboard de paridade, cutover de consentimento, canary de entrega |
| #10 | Teste de carga k6 com números publicáveis |

Critério do marco: EasyStok 100% no Hiram, taxa de entrega maior ou igual ao baseline, rollback testado, p99 publicado.

### Contínuo (não bloqueia marcos)

| Issue | Entrega |
|---|---|
| #9 | Dependabot e gate de CVE no CI |
| #20 | Rate limit e hardening de borda |
| #17 | Domínio hiram.dev |

## 3. Eficiência de desenvolvimento

Gargalos identificados e resposta:

1. **Integração só roda no CI.** A máquina atual não tem Docker, então os 43 arquivos de teste Testcontainers custam um push e 4 minutos de espera. Instalar Docker local (#18) é a maior alavanca de velocidade disponível.
2. **Main sem proteção.** 21 commits ficaram 5 dias sem CI e o docker build estava quebrado sem ninguém perceber. Branch protection com status check obrigatório (#19) e o hábito de push ao fechar cada passo eliminam a classe inteira do problema.
3. **Plano desatualizado esconde o progresso real.** Decidir o próximo passo exigia arqueologia. A reconciliação (#11) devolve o plano ao papel de fonte da verdade.
4. **Deploy manual.** Cada deploy consome atenção de operador e não deixa rastro. Registry + CD (#4, #8) transformam deploy em rotina de um comando.
5. **Sinal de segurança só local.** O NU1903 só apareceu no restore local. Dependabot e gate no CI (#9) trazem o alerta para onde ele é visto.

## 4. Riscos que este plano vigia

- **VM compartilhada com o ERP.** Orçamento de conexões e OOM do Postgres são os riscos do ADR-016. Validar antes do marco 3, com PgBouncer e oom_score_adj conforme o ADR.
- **Paridade inatingível sem canonicalização.** A regra de comparação de conteúdo precisa estar escrita antes de coletar as séries, senão a meta vira fantasia.
- **Console de demo é dev-only por design.** Servir /learn fora de Development exige decisão consciente de segurança (ambiente Demo dedicado), não um gate removido às pressas. Registrado na #15.
- **Perda de disco do RabbitMQ single-node.** Risco aceito no plano de absorção; o outbox limita a janela de perda a mensagens confirmadas e não consumidas.

## 5. Definição de go-live

O go-live está feito quando:

1. EasyStok emite 100% das notificações de email pelo Hiram, com callbacks fechando o status loop.
2. 30 dias de operação estável (métrica do MASTER-PLAN), zero notificação perdida.
3. Backup, restore, runbook e alertas comprovados, não apenas escritos.
4. Demo pública e roteiro comercial prontos para qualquer conversa de venda.
5. Números de carga publicados (p99 de ingestão), alimentando o pipeline de artigos.
