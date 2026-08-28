# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

CTMS — Centralised Translation Management System.

## Current state

This repository is a skeleton: it contains only `README.md`, `LICENSE` (MIT), and a
`.gitignore`. There is no source code, build system, or tests yet.

The `.gitignore` is the GitHub `VisualStudio.gitignore` template, so the project is
expected to be a .NET / Visual Studio (C#) solution. When application code is added,
update this file with:

- Build / run / test commands (e.g. `dotnet build`, `dotnet run`, `dotnet test`, and
  how to run a single test).
- The high-level architecture — the parts that require reading several files to grasp
  (solution/project layout, how translation data is stored and served, the API surface,
  and any background/sync components).
