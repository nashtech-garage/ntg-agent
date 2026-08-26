# Documentation index

Seven documents and two diagrams. They fall into three groups.

## Generative UI — A2UI and AG-UI

Read in this order if you are new to it.

| Document | What it answers | Read when |
|---|---|---|
| [A2UI-and-AG-UI.md](A2UI-and-AG-UI.md) | How a surface gets from the model to the screen — the four packages, the pipeline, Path A vs Path B, the action round trip | First. It is the conceptual map. |
| [AGUI-Surface-Template-Design.md](AGUI-Surface-Template-Design.md) | What a surface template should *be* — anatomy, naming, the data-model contract, three archetypes, the visual contract | Before designing a new surface |
| [Writing-a-SKILL.md](Writing-a-SKILL.md) | How to write, pack, import and test a skill | While building one |
| [skill-import-security.md](skill-import-security.md) | The ZIP importer's threat model, container and content controls, and what validation cannot defend | When changing the importer, or reviewing an upload path |

The short version of the distinction those documents keep returning to: **AG-UI is transport**
(an SSE event stream), **A2UI is the surface language** (a JSON description of components and a
data model). A skill's template is A2UI; it travels over AG-UI.

## Platform

| Document | What it answers |
|---|---|
| [agents-as-tools-architecture.md](agents-as-tools-architecture.md) | How outer agents delegate to inner agents, the single-table `AgentKind` design, the admin API and UI |
| [monitoring-setup.md](monitoring-setup.md) | Logging, metrics, traces, health checks — and which of them are wired up where |

## LightRAG (HTML diagrams)

Open these in a browser.

| Diagram | Subject |
|---|---|
| [lightrag-ingestion.html](lightrag-ingestion.html) | How an uploaded document is processed |
| [lightrag-ssh-tunnel.html](lightrag-ssh-tunnel.html) | Running LightRAG on the Azure VM over an SSH tunnel |

## Conventions

- Every document states what is **implemented** versus what is **planned**. Where a claim has
  been invalidated, the document says so rather than being quietly edited — see the
  "Claims that have expired" and "Corrections found during implementation" sections.
- Code references are paths from the repository root.
- The authority is always the code. Where a document and the source disagree, the source is
  right and the document is a bug.
