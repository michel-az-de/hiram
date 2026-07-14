# ADR-025: Gate de merge por label aprovado, com check de CI e branch protection

**Status:** Aceito
**Data:** 2026-07-14
**Decisores:** Felipe (arquiteto)

## Contexto

A Política v4.0 do fluxo de tarefas separa PRs em tier baixo (chore, docs, test, fix trivial), que
podem auto-mergear no verde, e tier alto (feat, refactor, migração, auth, RLS), que exigem aprovação
humana explícita antes do merge, sinalizada por um label `aprovado`. Até aqui o gate era só disciplina:
nada no GitHub impedia um merge de tier alto sem aprovação, e `gh pr merge --auto` não tinha requisito
para segurar.

Duas restrições do GitHub tornam a proteção nativa insuficiente:

1. Branch protection e rulesets não sabem exigir a presença de um label como condição de merge. As
   condições nativas são checks de status obrigatórios, aprovações de review, branch atualizada e
   assinatura.
2. O repositório é de dono único. O GitHub não permite que o autor aprove o próprio PR, então exigir
   uma aprovação de review trava todo PR do mantenedor solo.

Sem uma dessas, `gh pr merge --auto` num repo sem proteção mergeia assim que os checks ficam verdes,
sem qualquer gesto humano, que é exatamente o que o tier alto quer evitar.

## Decisão

Implementar o gate de tier alto como um check de CI próprio, `gate-aprovado`, exigido pela branch
protection de `main`. O check falha enquanto o label `aprovado` não estiver no PR e passa quando ele é
aplicado, re-executando nos eventos de label. A branch protection de `main` passa a exigir os checks de
CI existentes (`build-and-test`, `docker-images`, `deploy-manifests`) mais o `gate-aprovado`, além de
bloquear push direto e exigir PR.

Com isso, `gh pr merge <pr> --auto --squash --delete-branch` segura o merge de qualquer PR até que o
label `aprovado` seja aplicado por um humano, sem depender de aprovação de review (que trava o autor
solo) e sem depender só de disciplina. Para PRs de tier baixo o fluxo segue mergeando via `gh pr merge`
com o label aplicado no mesmo passo, sem esperar revisor externo.

## Opções consideradas

### Opção A: check de CI dirigido por label (escolhida)

**Prós:** expressa o label da Política v4.0 nativamente como requisito de merge, funciona em repo solo,
o `--auto` passa a ter o que esperar, e o gate fica versionado no repositório.
**Contras:** um workflow a mais, e o label vira parte do caminho crítico de merge.

### Opção B: exigir aprovação de review

**Prós:** mecanismo nativo, sem workflow novo.
**Contras:** o autor não aprova o próprio PR, então trava o mantenedor solo. Rejeitada.

### Opção C: manter só disciplina, sem proteção

**Prós:** zero configuração.
**Contras:** nada impede merge de tier alto sem aprovação, e `--auto` não tem gate. Rejeitada, é o
problema que este ADR resolve.

## Consequências

- **Fica mais fácil:** armar `--auto` em PRs de tier alto sabendo que só mergeiam após o `aprovado`, e
  ter o gate auditável e versionado.
- **Fica mais difícil:** todo PR passa a depender do check `gate-aprovado`; um PR sem o label fica
  pendente por design, inclusive os de tier baixo, que aplicam o label ao finalizar.

## Gatilho de revisão

Migração para organização com mais mantenedores (aprovação de review passa a ser viável e o label pode
virar redundante), adoção de merge queue, ou mudança do modelo de tiers da Política.
