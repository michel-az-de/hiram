# Changelog

Todas as mudancas relevantes deste projeto sao documentadas aqui.
Formato baseado em Keep a Changelog (https://keepachangelog.com/pt-BR/1.1.0/).

## [Unreleased]

### Added
- Stack conjunta: redirect `www` -> apex no Caddy e plumbing dos flags `SITE_INDEXABLE`/`NEWSLETTER_ENABLED` no servico `levante-web`, habilitando o cutover D0 do dominio do Levante (`felipemichel.com`, apex canonico). Ver Levante `docs/adr/0007` e `docs/cutover-felipemichel-com.md`.

### Changed
- Adota o Protocolo Operacional v4.0 (PR-first, issue-driven, auto-merge por tier). Ver ADR de adocao e CLAUDE.md.