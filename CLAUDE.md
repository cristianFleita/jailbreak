# Game Jam Project — CLAUDE.md

## Project Overview
Multiplayer 3D game for vibe jam.
Engine: Unity 3D (WebGL export)
Frontend: React + Vite + TypeScript + Chakra UI
Backend: Node.js + Express + Socket.io
Database: PostgreSQL (Vercel)
Deploy: React→Vercel, Backend→Render

## Tech Stack Constraints
- Unity version: Unity 6 LTS
- Node: v18+
- React: 18+ with Vite
- TypeScript: strict mode
- No global npm packages — everything local to project

## Active Engine Specialists
Use the UNITY agent set (not Godot or Unreal):
- unity-specialist (lead)
- unity-dots-ecs
- unity-shaders-vfx
- unity-ui-toolkit

## Memory System
ALWAYS read memory/progress.md at session start.
ALWAYS append to memory/session-log.md at session end.
ALWAYS write key decisions to memory/decisions.md.

## Local AI (Ollama)
For testing and documentation tasks, delegate to Ollama:
  ollama run gemma3:4b "<prompt>"
Use this for: writing tests, generating docs, code comments.
Do NOT use Ollama for architecture decisions or complex code.

## Agent Coordination Rules
1. Ask before proposing — never assume
2. Show 2-3 options with tradeoffs
3. Wait for user approval before writing files
4. Cross-domain changes → coordinate through producer agent
5. Never modify files outside your domain without explicit delegation