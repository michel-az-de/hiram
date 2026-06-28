# Ledger de calibração

Registro de probabilidade estimada versus resultado real por passo do plano de absorção do EasyStok
(plans/easystok-absorcao-total.md). Atualizar é parte do Definition of Done de cada passo.

P = M x C x R (maturidade do componente x cobertura de teste do caminho x precedente copiável).
Detalhe da fórmula na seção de medição do plano. Após cada passo, comparar o estimado com o real
calibra os pesos do próximo.

| Passo | P estimado | Fechou de primeira | Nota |
|---|---|---|---|
| 0.3 Data Protection key ring compartilhado | 0.80 | Sim | Fix localizado em `AddHiramDataProtection` (SetApplicationName mais PersistKeysToFileSystem) e wiring nos dois hosts via `DataProtection:KeysPath`. O teste cross-process exigiu um probe dedicado para ser um segundo processo do SO real, não in-proc. Vermelho reproduziu o bug exato (key not found in key ring), verde passou. Release sem warning, unit 112/112. Suíte Testcontainers não roda no dev local (Docker ausente), validação no CI. |
